/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012, Telekom Deutschland GmbH 
*
* This file is part of RELOAD.NET.
*
* RELOAD.NET is free software: you can redistribute it and/or modify
* it under the terms of the GNU Lesser General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* RELOAD.NET is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU Lesser General Public License for more details.
*
* You should have received a copy of the GNU Lesser General Public License
* along with RELOAD.NET.  If not, see <http://www.gnu.org/licenses/>.
*
* see https://github.com/RELOAD-NET/RELOAD.NET
* 
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using TSystems.RELOAD.ForwardAndLinkManagement;
using Microsoft.Ccr.Core;
using System.Security.Cryptography;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Extension;
using FTEntry = TSystems.RELOAD.Topology.TopologyPlugin.RoutingTable.FTableEntry;

namespace TSystems.RELOAD.Transport {
  #region Transport

  public class MessageTransport {

    #region Properties

    private Dictionary<UInt64, SortedDictionary<UInt32, MessageFragment>> fragmentedMessageBuffer = new Dictionary<ulong, SortedDictionary<uint, MessageFragment>>();

    private DispatcherQueue m_DispatcherQueue;
    private Machine m_machine;
    private TopologyPlugin m_topology;
    private ReloadConfig m_ReloadConfig = null;

    /// <summary>
    /// Notifies about store status
    /// </summary>
    private Port<ReloadDialog> storeDone;
    public Port<ReloadDialog> StoreDone {
      get { return storeDone; }
      set { storeDone = value; }
    }

    /// <summary>
    /// Notifies about fetch status
    /// </summary>
    private Port<List<IUsage>> fetchDone;
    public Port<List<IUsage>> FetchDone {
      get { return fetchDone; }
      set { if (value != null) fetchDone = value; }
    }

    /// <summary>
    /// Notifies about AppAttach status
    /// </summary>
    private Port<IceCandidate> appAttachDone;
    public Port<IceCandidate> AppAttachDone {
      get { return appAttachDone; }
      set { if (value != null) appAttachDone = value; }
    }

    private IForwardLinkManagement m_flm;
    public IForwardLinkManagement GetForwardingAndLinkManagementLayer() {
      return m_flm;
    }

    private ForwardingLayer m_forwarding;
    private Statistics m_statistics;

    #endregion

    public void Init(Machine machine) {
      m_machine = machine;
      m_topology = machine.Topology;
      m_forwarding = machine.Forwarding;
      m_flm = machine.Interface_flm;
      m_DispatcherQueue = machine.ReloadConfig.DispatcherQueue;
      m_ReloadConfig = machine.ReloadConfig;
      m_statistics = m_ReloadConfig.Statistics;
    }

    public ReloadFLMEventArgs rfm_ReloadFLMEventHandler(object sender, ReloadFLMEventArgs args) {
      if (args.Eventtype == ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK) {
        receive_message(args.Message);
      }
      return args;
    }

    public IEnumerator<ITask> SendPing(Destination dest, PingOption pingOption) {
      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;

      reloadSendMsg = create_ping_req(dest);

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

      Boolean NextHopIsDestination = false;
      Node NextHopNode = NextHopToDestination(dest, ref NextHopIsDestination);

      if (NextHopNode == null)
        yield break;

      if (NextHopNode.Id == m_topology.LocalNode.Id)
        yield break;

      if (dest.type == DestinationType.node) {
        /* This code assumes, that a node we want to ping is already attached and added to the routing table */
        NodeState nodestate = m_topology.routing_table.GetNodeState(dest.destination_data.node_id);
        bool pinging = m_topology.routing_table.GetPing(dest.destination_data.node_id);

        if (((pingOption & PingOption.standard | PingOption.finger) != 0) && nodestate == NodeState.unknown && !pinging) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Ignoring redundant Ping for {0}", dest));
          yield break;
        }
        m_topology.routing_table.SetPinging(dest.destination_data.node_id, true, false);
      }
      else if (!NextHopIsDestination && ((pingOption & PingOption.direct) != 0)) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Direct Ping for {0} ignored, no entry in routing table", dest));
        yield break;
      }


      /* Don't spend too much time on connectivity checks */
      int iRetrans = 3;

      while (iRetrans > 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), NextHopNode.Id, reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Ping: " + ex.Message);
          yield break;
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        /*              If a response has not been received when the timer fires, the request
                        is retransmitted with the same transaction identifier.
        */
        --iRetrans;
      }

      try {
        PingReqAns answ = null;

        if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

          if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Ping_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

            answ = (PingReqAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              if ((pingOption & PingOption.finger) != 0) {
                foreach (Topology.TopologyPlugin.RoutingTable.FTableEntry fte in m_topology.routing_table.FingerTable) {
                  if (fte.Finger == dest.destination_data.ressource_id) {
                    fte.dtLastSuccessfullFinger = DateTime.Now;
                    fte.Successor = reloadRcvMsg.OriginatorID;
                    fte.pinging = true;
                  }
                }

                /* Attach if not attached */
                if (!m_topology.routing_table.IsAttached(reloadRcvMsg.OriginatorID))
                  Arbiter.Activate(m_DispatcherQueue,
                      new IterativeTask<Destination, NodeId, AttachOption>(new Destination(reloadRcvMsg.OriginatorID),
                          reloadRcvMsg.LastHopNodeId,
                          AttachOption.sendping,
                          AttachProcedure));
                else if (reloadRcvMsg.OriginatorID != reloadRcvMsg.LastHopNodeId)// Send ping to get/keep a physical connection
                  Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(reloadRcvMsg.OriginatorID), PingOption.direct, SendPing));
              }
            }
          }
        }
        else {
          if (dest.type == DestinationType.node) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Ping failed: removing node {0}", dest.destination_data.node_id));
            m_topology.routing_table.Leave(dest.destination_data.node_id);
          }
          m_statistics.IncTransmissionError();
        }

        if (dest.type == DestinationType.node)
          m_topology.routing_table.SetPinging(dest.destination_data.node_id, false, answ != null);
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Ping: " + ex.Message);
      }
    }

    public IEnumerator<ITask> AttachProcedure(Destination dest, NodeId NextHopId, AttachOption attach_option) {
      ReloadMessage reloadSendMsg;

      /* 9.5.1 
       *     When a peer needs to Attach to a new peer in its neighbor table, it
             MUST source-route the Attach request through the peer from which it
             learned the new peer's Node-ID.  Source-routing these requests allows
             the overlay to recover from instability.
      */

      reloadSendMsg = create_attach_req(dest, (attach_option & AttachOption.forceupdate) != 0);


      if (dest.type == DestinationType.node) {
        NodeState nodestate = m_topology.routing_table.GetNodeState(dest.destination_data.node_id);

        if (nodestate == NodeState.attaching) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Ignoring redundant Attach for {0}", dest));
          yield break;
        }
      }

      Boolean NextHopIsDestination = false;
      Node NextHopNode = null;

      if (NextHopId != null)
        NextHopNode = m_topology.routing_table.GetNode(NextHopId);

      if (NextHopNode == null)
        NextHopNode = NextHopToDestination(dest, ref NextHopIsDestination);

      if (NextHopNode == null)
        yield break;

      if (dest.type == DestinationType.node)
        m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.attaching);

      ReloadDialog reloadDialog = null;

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} via {1} ==> Dest={2} TransId={3:x16}",
                                                                                  RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
                                                                                  NextHopNode,
                                                                                  dest.ToString(),
                                                                                  reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AttachProcedure: " + ex.Message);
          yield break;
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });


        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
        if (iRetrans >= 0) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} SendAttach  via {1} TransId={2:x16}", iRetrans, NextHopNode, reloadSendMsg.TransactionID));
          m_ReloadConfig.Statistics.IncRetransmission();
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! SendAttach  via {0} TransId={1:x16}", NextHopNode, reloadSendMsg.TransactionID));
          m_ReloadConfig.Statistics.IncTransmissionError();

          if (dest.destination_data.node_id != null)
            m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
        }
      }

      try {
        if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          /*the SourceNodeID delivered from reloadDialog comes from connection
           * table and is the last hop of the message
           */
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
          RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
          if (msgCode == RELOAD_MessageCode.Attach_Answer) {
            AttachReqAns answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

            /* TKTODO
             * 1.  The response to a message sent to a specific Node-ID MUST have
                   been sent by that Node-ID.
               2.  The response to a message sent to a Resource-Id MUST have been
                   sent by a Node-ID which is as close to or closer to the target
                   Resource-Id than any node in the requesting node's neighbor
                   table.
            */
            if (answ != null) {
              Node Originator = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);

              /*  An Attach in and of itself does not result in updating the routing
               *  table of either node.
               *  Note: We use the routing table here only for storing ice candidates 
               *  for later use, we will not update successor or predecessor list
               */

              m_topology.routing_table.AddNode(Originator);
              m_topology.routing_table.SetNodeState(Originator.Id, NodeState.attached);

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                String.Format("{0} <== {1} last={2} TransId={3:x16}",
                msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                reloadRcvMsg.LastHopNodeId, reloadRcvMsg.TransactionID));

              if ((CheckAndSetAdmittingPeer(Originator) &&
                Originator.Id != reloadRcvMsg.LastHopNodeId)
                  || (attach_option & AttachOption.sendping) != 0) {
                // Send ping to get a physical connection
                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(Originator.Id), PingOption.direct, SendPing));
              }
            }
          }
          else if (msgCode == RELOAD_MessageCode.Error) {
            if (dest.type == DestinationType.node) {
              ErrorResponse error = (ErrorResponse)reloadRcvMsg.reload_message_body;

              if (error != null) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  String.Format("AttachProcedure: Got Error {0} from {1} deleting {2}",
                    error.ErrorMsg,
                    reloadRcvMsg.OriginatorID,
                    dest.destination_data.node_id));

                m_topology.routing_table.Leave(dest.destination_data.node_id);
              }
            }
          }
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AttachProcedure: " + ex.Message);

        if (dest.destination_data.node_id != null)
          m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
      }
    }

    public IEnumerator<ITask> AppAttachProcedure(Destination dest) {
      ReloadMessage reloadSendMsg;

      reloadSendMsg = create_app_attach_req(dest);

      if (dest.type != DestinationType.node) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure failed: unexpected destination type"));
        yield break;
      }

      if (dest.destination_data.node_id == m_topology.Id) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("local AppAttachProcedure dropped"));
        yield break;
      }

      Node node = m_topology.routing_table.FindNextHopTo(dest.destination_data.node_id, true, false);

      if (node == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure: failed, did not found next hop to {0}", dest.destination_data.node_id));
        yield break;
      }

      ReloadDialog reloadDialog = null;

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
      int iRetrans = ReloadGlobals.MaxRetransmissions;

      m_topology.routing_table.SetNodeState(dest.destination_data.node_id,
        NodeState.attaching);

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("{0} via {1} ==> Dest={2} TransId={3:x16}",
            RELOAD_MessageCode.App_Attach_Request.ToString().PadRight(16, ' '),
            node, dest.ToString(), reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
            reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
            RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });


        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

          if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.App_Attach_Answer) {
            AppAttachReqAns answ = (AppAttachReqAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              node = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);
              /*  An Attach in and of itself does not result in updating the routing
               *  table of either node.
               *  Note: We use the routing table here only for storing ice candidates 
               *  for later use, we will not update successor or predecessor list
               */
              m_topology.routing_table.AddNode(node);
              if (m_topology.routing_table.GetNodeState(node.Id) < NodeState.attached)
                m_topology.routing_table.SetNodeState(node.Id, NodeState.attached);

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} last={2} TransId={3:x16}",
                  reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.LastHopNodeId, reloadRcvMsg.TransactionID));

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
                  String.Format("AppAttach returns IP: {0}",
                      answ.ice_candidates[0].addr_port.ipaddr.ToString()));
              /* Proprietary registry interface function to support external clients */
              ReloadGlobals.StoreRegAnswer("sip:" + answ.ice_candidates[0].addr_port.ipaddr.ToString() + ":5060");
              m_ReloadConfig.ConnEstEnd = DateTime.Now;
              if (AppAttachDone != null) appAttachDone.Post(answ.ice_candidates[0]);
              if (ReloadGlobals.AutoExe) {
                TimeSpan appAttachTime = DateTime.Now - m_ReloadConfig.StartFetchAttach;
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, String.Format("Fetch:{0}", appAttachTime.TotalSeconds));
              }
            }
          }
          else if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Error) {
            // TODO
          }
        }
        else {
          m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
        m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
      }
    }

    public IEnumerator<ITask> PreJoinProdecure(List<BootstrapServer> BootstrapServerList) {
      bool attached = false;

      if (m_topology.LocalNode.Id == null)
        yield break;

      ReloadMessage reloadSendMsg;
      ReloadMessage reloadRcvMsg = null;

      /* This is the begin of populating the neighbor table
         convert local node to resource id + 1 and sent an attach to it
       */
      Destination dest = new Destination(new ResourceId(
        m_topology.LocalNode.Id + (byte)1));
      Destination last_destination = null;
      Node NextHopNode = null;
      int succSize = m_ReloadConfig.IamClient ? 1 : ReloadGlobals.SUCCESSOR_CACHE_SIZE;
      for (int i = 0; i < succSize; i++) {
        if (last_destination != null && last_destination == dest)
          break;
        if (m_ReloadConfig.IamClient)
          reloadSendMsg = create_attach_req(dest, false);
        else
          reloadSendMsg = create_attach_req(dest, true);

        //we do not know the bootstrap peer's node id so far so we leave that parameter empty Node(null)

        ReloadDialog reloadDialog = null;
        int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

        /* Modification for Clients out of draft, TLS stack will take some time
         * to initialize, add another 10s waiting */
        if (m_ReloadConfig.IamClient && i == 0)
          RetransmissionTime += 10000;

        int iRetrans = ReloadGlobals.MaxRetransmissions;

        int iCycleBootstrap = 0;

        while (iRetrans >= 0 &&
          m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
          /* This is the first bootstrap contacting sequence if NextHopNode 
           * is still zero, in any other case 
           * use an attach to the node where we got the last answer from 
           */
          if (NextHopNode == null) {
            /* we could use a foreach loop, but CCR would multitask it, but we
             * want serialize that here
            */
            if (iCycleBootstrap >= BootstrapServerList.Count())
              iCycleBootstrap = 0;

            BootstrapServer bss = BootstrapServerList[iCycleBootstrap++];

            if (attached == true)
              break;

            //TKTODO Rejoin of bootstrap server not solved
            List<IceCandidate> ics = new List<IceCandidate>();
            IceCandidate ice = new IceCandidate(new IpAddressPort(
              AddressType.IPv4_Address, ReloadGlobals.IPAddressFromHost(
              m_ReloadConfig, bss.Host), (UInt16)bss.Port),
              Overlay_Link.TLS_TCP_FH_NO_ICE);

            ics.Add(ice);

            NextHopNode = new Node(reloadRcvMsg == null ?
              null : reloadRcvMsg.OriginatorID, ics);
          }

          try {
            /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} ==> {1} Dest={2} TransId={3:x16}",
              RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
              NextHopNode, dest.ToString(), reloadSendMsg.TransactionID));

            Arbiter.Activate(m_DispatcherQueue,
              new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
              reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
              RetransmissionTime, reloadDialog.Execute));
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              "PreJoinProcedure: " + ex.Message);
          }

          yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

          if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
            break;


          /* still bootstrapping, allow cycling trough different bootstraps by
           * resetting NextHopNode
           */
          if (i == 0)
            NextHopNode = null;

          /* If a response has not been received when the timer fires, the request
             is retransmitted with the same transaction identifier. 
          */
          --iRetrans;
          if (iRetrans >= 0) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} Attach  {1} TransId={2:x16}", iRetrans, NextHopNode, reloadSendMsg.TransactionID));
            m_statistics.IncRetransmission();
          }
          else {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! Attach {0} TransId={1:x16}", NextHopNode, reloadSendMsg.TransactionID));
            m_statistics.IncTransmissionError();
            if (ReloadGlobals.AutoExe) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoin: Exit because initial Attach Faild!");
              m_machine.SendCommand("Exit");
            }
          }
        }
        try {
          if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
            //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
            reloadRcvMsg = reloadDialog.ReceivedMessage;
            RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
            if (reloadRcvMsg != null) {
              if (msgCode == RELOAD_MessageCode.Attach_Answer) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                  String.Format("{0} <== {1} last={2} TransId={3:x16}",
                  msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                  reloadRcvMsg.LastHopNodeId, reloadRcvMsg.TransactionID));

                AttachReqAns answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

                if (answ != null) {
                  m_ReloadConfig.State = ReloadConfig.RELOAD_State.PreJoin;
                  m_machine.StateUpdates(ReloadConfig.RELOAD_State.PreJoin);

                  /*  An Attach in and of itself does not result in updating the routing
                   *  table of either node.
                   *  Note: We use the routing table here only for storing ice candidates 
                   *  for later use, we will not update successor or predecessor list
                   */
                  NextHopNode = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);
                  /*  An Attach in and of itself does not result in updating the routing
                   *  table of either node.
                   *  Note: We use the routing table here only for storing ice candidates 
                   *  for later use, we will not update successor or predecessor list
                   */
                  m_topology.routing_table.AddNode(NextHopNode);
                  m_topology.routing_table.SetNodeState(NextHopNode.Id,
                    NodeState.attached);

                  if (CheckAndSetAdmittingPeer(NextHopNode) &&
                    NextHopNode.Id != reloadRcvMsg.LastHopNodeId)
                    // Send ping to establish a physical connection
                    Arbiter.Activate(m_DispatcherQueue,
                      new IterativeTask<Destination, PingOption>(new Destination(
                      NextHopNode.Id), PingOption.direct, SendPing));

                  if (m_ReloadConfig.IamClient) {
                    m_ReloadConfig.LastJoinedTime = DateTime2.Now;
                    TimeSpan joiningTime = m_ReloadConfig.LastJoinedTime - m_ReloadConfig.StartJoinMobile;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                      "Join:" + joiningTime.TotalSeconds.ToString());
                  }

                  attached = true;
                }
              }
              else if (msgCode == RELOAD_MessageCode.Error) {
                if (dest.type == DestinationType.node) {
                  ErrorResponse error = (ErrorResponse)reloadRcvMsg.reload_message_body;

                  if (error != null) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      String.Format("Prejoin: Got Error {0} from {1} deleting {2}",
                        error.ErrorMsg,
                        reloadRcvMsg.OriginatorID,
                        dest.destination_data.node_id));

                    m_topology.routing_table.Leave(dest.destination_data.node_id);
                  }
                }
              }
            }
            else {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: reloadRcvMsg == null!!");
            }

            last_destination = dest;
            dest = new Destination(new ResourceId(reloadRcvMsg.OriginatorID) + (byte)1);
          }
          else
            break;
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: " + ex.Message);
        }
      } // End Successor Search

      // FingerTable enrichment
      if (!m_ReloadConfig.IamClient) {
        List<FTEntry> fingers = m_topology.routing_table.AttachFingers();
        Port<bool> attachNextPort = null;
        Boolean attachNext = true;
        /* JP SHOULD send Attach requests to initiate connections to each of
         * the peers in the neighbor table as well as to the desired finger
         * table entries.
         */
        foreach (FTEntry finger in fingers) {
          attachNextPort = new Port<bool>();
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<FTEntry, Port<bool>>(
            finger, attachNextPort, AttachFinger));
          /* Wait for finger attach */
          yield return Arbiter.Receive(false, attachNextPort, next => {
            attachNext = next;
          });
          if (!attachNext)
            break;

        }
      }
      /* see base -18 p.106
      /* 4.  JP MUST enter all the peers it has contacted into its routing
      /*     table.
       */
      m_topology.routing_table.Conn2Route();

      /* Once JP has a reasonable set of connections it is ready to take its 
       * place in the DHT.  It does this by sending a Join to AP.
       */
      if (m_ReloadConfig.AdmittingPeer != null)
        if (!m_ReloadConfig.IamClient) {
          m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joining;
          m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joining);

          m_topology.routing_table.SetWaitForJoinAnsw(
            m_ReloadConfig.AdmittingPeer.Id, true);

          reloadSendMsg = create_join_req(
            new Destination(m_ReloadConfig.AdmittingPeer.Id));
          ReloadDialog reloadDialog = null;

          int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
          int iRetrans = ReloadGlobals.MaxRetransmissions;

          while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, m_ReloadConfig.AdmittingPeer);

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}", RELOAD_MessageCode.Join_Request.ToString().PadRight(16, ' '), m_ReloadConfig.AdmittingPeer, reloadSendMsg.TransactionID));

            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
            yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

            if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
              break;

            /* If a response has not been received when the timer fires, the request
               is retransmitted with the same transaction identifier. 
            */
            --iRetrans;
            if (iRetrans >= 0) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} Join  {1} TransId={2:x16}", iRetrans, m_ReloadConfig.AdmittingPeer, reloadSendMsg.TransactionID));
              m_statistics.IncRetransmission();
            }
            else {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! Join {0} TransId={1:x16}", m_ReloadConfig.AdmittingPeer, reloadSendMsg.TransactionID));
              m_statistics.IncTransmissionError();
            }
          }

          try {
            if (!reloadDialog.Error) {
              reloadRcvMsg = reloadDialog.ReceivedMessage;
              RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
              if (reloadRcvMsg != null) {
                if (msgCode == RELOAD_MessageCode.Join_Answer) {
                  m_topology.routing_table.SetWaitForJoinAnsw(reloadRcvMsg.OriginatorID, false);

                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                    String.Format("{0} <== {1} TransId={2:x16}",
                    msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                    reloadRcvMsg.TransactionID));

                  NodeState nodestate = m_topology.routing_table.GetNodeState(reloadRcvMsg.OriginatorID);

                  if (nodestate == NodeState.updates_received) {
                    /* we previously received an update from admitting peer (maybe 
                     * race condition), now joining is complete in any other case 
                     * wait for updates to come from this node */
                    m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joined;
                    m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joined);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("Joining completed"));
                    m_ReloadConfig.LastJoinedTime = DateTime.Now;
                    TimeSpan joiningTime = m_ReloadConfig.LastJoinedTime - m_ReloadConfig.StartJoining;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, String.Format("Join:{0}", joiningTime.TotalSeconds.ToString()));

                    m_topology.routing_table.SendUpdateToAllNeighbors();
                  }
                  else {
                    m_ReloadConfig.LastJoinedTime = DateTime.Now;
                    TimeSpan joiningTime = m_ReloadConfig.LastJoinedTime - m_ReloadConfig.StartTime;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, String.Format("Join:{0}", joiningTime.TotalSeconds.ToString()));

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                      String.Format("Prejoin: nodestate != update_recv at Node {0}", m_machine.ReloadConfig.ListenPort));
                  }
                  //m_topology.routing_table.SendUpdatesToAllFingers();
                }
              }
              else {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: reloadRcvMsg == null!!");
              }
            }
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: " + ex.Message);
          }
        }
        else {
          if (m_ReloadConfig.SipUri == "")
            m_ReloadConfig.SipUri = String.Format("{0}@{1}", ReloadGlobals.HostName,
                ReloadGlobals.OverlayName);

          if (m_ReloadConfig.SipUri != null && m_ReloadConfig.SipUri != "") {
            // explictite SIP registration as minimal config for RELOAD clients

            IUsage sipRegistration = m_machine.UsageManager.CreateUsage(Usage_Code_Point.SIP_REGISTRATION,
                                                             2,
                                                             m_ReloadConfig.SipUri);
            sipRegistration.ResourceName = m_ReloadConfig.SipUri;
            List<StoreKindData> clientRegistrationList = new List<StoreKindData>();
            StoreKindData sipKindData = new StoreKindData(sipRegistration.KindId,
                                                          0, new StoredData(sipRegistration.Encapsulate(true)));
            clientRegistrationList.Add(sipKindData);
            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<string, List<StoreKindData>>(m_ReloadConfig.SipUri, clientRegistrationList, Store));
          }
        }
      else {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
          String.Format("PreJoinPredure => Node {0} has no admitting peer = {1}!",
          m_machine.ReloadConfig.ListenPort, m_ReloadConfig.AdmittingPeer));
        if (ReloadGlobals.AutoExe) {
          m_machine.SendCommand("Exit");
        }
      }
    } // End PreJoin        

    /// <summary>
    /// Attaches to a node needed to enrich the finger table.
    /// </summary>
    /// <param name="finger"></param>    
    /// <returns></returns>
    public IEnumerator<ITask> AttachFinger(FTEntry finger,
      Port<bool> attachNext) {
      Destination dest = new Destination(finger.Finger);
      ReloadMessage reloadSendMsg = create_attach_req(dest, false);
      ReloadDialog reloadDialog = null;

      int RetransmissionTime = ReloadGlobals.RetransmissionTime +
        ReloadGlobals.MaxTimeToSendPacket;
      int iRetrans = ReloadGlobals.MaxRetransmissions;

      Boolean NextHopIsDestination = false;
      var myNodeId = m_topology.LocalNode.Id;
      Node nextHopNode = NextHopToDestination(dest, ref NextHopIsDestination);

      if (nextHopNode == null ||
        nextHopNode.Id == m_ReloadConfig.LocalNodeID)
        nextHopNode = m_ReloadConfig.AdmittingPeer;

      if (nextHopNode == null) //{
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
        String.Format("No Route to Finger! {0}", finger.Finger));
      //attachNext.Post(true);
      // yield break;
      //}
      //else {
      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, nextHopNode);
          m_forwarding.LoopedTrans = new Port<UInt64>();
          if (reloadDialog == null)
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            "ReloadDialog null!");

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
            String.Format("Finger-{0} via {1} ==> Dest={2} TransId={3:x16}",
            RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
            nextHopNode, dest.ToString(), reloadSendMsg.TransactionID));

<<<<<<< HEAD
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
            new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime,
            reloadDialog.Execute));
        }
        catch (Exception e) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("AttachFinger: " + e));
          attachNext.Post(true);
          yield break;
        }
        bool gotLoop = false;

        yield return Arbiter.Choice(
          /* Success, Attached to finger */
          Arbiter.Receive(false, reloadDialog.Done, done => { }),
          /* Loop detected */
          Arbiter.Receive(false, m_forwarding.LoopedTrans, transId => {
            if (transId == reloadSendMsg.TransactionID) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                String.Format("Not re-sending transaction: {0:x16}=> a loopback detected!",
                reloadSendMsg.TransactionID));
              gotLoop = true;
              m_forwarding.LoopedTrans = new Port<ulong>();
            }
          }));
=======
            Arbiter.Activate(m_DispatcherQueue,
              new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
              new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime,
              reloadDialog.Execute));
          }
          catch (Exception e) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("AttachFinger: " + e));
          }
          bool gotLoop = false;

          if (reloadDialog == null)
            yield break;      

          yield return Arbiter.Choice(
            /* Success, Attached to finger */
            Arbiter.Receive(false, reloadDialog.Done, done => { }),
            /* Loop detected */
            Arbiter.Receive(false, m_forwarding.LoopedTrans, transId => {
              if (transId == reloadSendMsg.TransactionID) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                  String.Format("Not re-sending transaction: {0:x16}=> a loopback detected!",
                  reloadSendMsg.TransactionID));
                gotLoop = true;
                m_forwarding.LoopedTrans = new Port<ulong>();
              }
            }));
>>>>>>> c72920f5592677c84932e6ebf9afc0acefa648a4

        if (gotLoop) {
          attachNext.Post(true);
          yield break; ;
        }

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        // If a response has not been received when the timer fires, the request
        // is retransmitted with the same transaction identifier. 

        --iRetrans;
        if (iRetrans >= 0) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
            String.Format("Retrans {0} SendAttach  via {1} TransId={2:x16}",
            iRetrans, nextHopNode, reloadSendMsg.TransactionID));
          m_ReloadConfig.Statistics.IncRetransmission();
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("Failed! SendAttach  via {0} TransId={1:x16}",
            nextHopNode, reloadSendMsg.TransactionID));
          m_ReloadConfig.Statistics.IncTransmissionError();
          if (dest.destination_data.node_id != null)
            m_topology.routing_table.SetNodeState(dest.destination_data.node_id,
              NodeState.unknown);
        }
        // }
      }

      try {
        if (reloadDialog != null && !reloadDialog.Error &&
          reloadDialog.ReceivedMessage != null) {
          /*the SourceNodeID delivered from reloadDialog comes from connection
           * table and is the last hop of the message
           */
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
          RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
          AttachReqAns answ = null;
          if (msgCode == RELOAD_MessageCode.Attach_Answer) {
            answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
              String.Format("Finger-{0} <== {1} TransId={2:x16}",
              msgCode.ToString().PadRight(16, ' '),
              reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

            Node originator = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);
            NodeState nodeState = m_topology.routing_table.GetNodeState(originator.Id);
            if (nodeState == NodeState.unknown) {
              attachNext.Post(true);
              m_topology.routing_table.AddNode(originator);
              m_topology.routing_table.SetNodeState(originator.Id, NodeState.attached);
              m_topology.routing_table.AddFinger(originator, NodeState.attached);
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
                String.Format("Got a finger! {0}", originator.Id));
            }
            /* we know this peer, further Attaches will return same peer */
            else
              attachNext.Post(false);
          }
        }
      }
      catch (Exception e) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          String.Format("AttachFingers:" + e));
      }
    }

    /// <summary>
    /// Stores the Usage data in the RELOAD overlay
    /// </summary>
    /// <param name="ResourceName"></param>
    /// <param name="DestUrl"></param>
    /// <param name="exists">if true, stores the values, else stores a "non-existent" value.</param>
    /// <param name="usages">The Usage data to be stored.</param>
    /// <returns></returns>        
    public IEnumerator<ITask> Store(string ResourceName, List<StoreKindData> kind_data) {
      if (m_ReloadConfig.IamClient)
        m_ReloadConfig.StartStoreMobile = DateTime2.Now;
      else
        m_ReloadConfig.StartStore = DateTime.Now;

      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;
      ResourceId res_id = new ResourceId(ResourceName);

      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1}", ResourceName, res_id));
      Node node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

      if (m_ReloadConfig.IamClient && node == null) {
        node = m_ReloadConfig.AdmittingPeer;
      }
      if (node == null || node.Id == m_ReloadConfig.LocalNodeID) {
        foreach (StoreKindData storeKindData in kind_data) {

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
            String.Format("Local storage at NodeId {0}", node.Id));
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
            "Store:0,011111");
          m_topology.Store(res_id, storeKindData);
        }
        if (storeDone != null) storeDone.Post(reloadDialog);
        yield break;
      }
      reloadSendMsg = create_store_req(new Destination(res_id), kind_data);

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue,
              new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
                  new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          storeDone.Post(reloadDialog);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
          ReloadGlobals.PrintException(m_ReloadConfig, ex);
          break;
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;


        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

          if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
              String.Format("Store successful:{0} <== {1} TransId={2:x16}",
              reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

            m_ReloadConfig.EndStore = DateTime.Now;

            TimeSpan storeSpan = storeSpan = m_ReloadConfig.EndStore - m_ReloadConfig.StartStore;

            if (m_ReloadConfig.IamClient)
              storeSpan = DateTime2.Now - m_ReloadConfig.StartStoreMobile;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                                  String.Format("Store:{0}",
                                  storeSpan.TotalSeconds.ToString()));

            if (storeDone != null) storeDone.Post(reloadDialog);
          }
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
            String.Format("Store failed"));
          m_statistics.IncTransmissionError();
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store 2: " + ex.Message);
        ReloadGlobals.PrintException(m_ReloadConfig, ex);
      }

      if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Shutdown)
        m_machine.Finish();

    }

    #region Proprietary
    // --josch

    /// <summary>
    /// Proprietary: Stores the Usage data in a different RELOAD overlay using viaGateWay as gateway
    /// </summary>
    /// <param name="ResourceName"></param>
    /// <param name="kind_data"></param>
    /// <param name="viaGateWay"></param>
    /// <returns></returns>
    public IEnumerator<ITask> Store(string ResourceName, List<StoreKindData> kind_data, NodeId viaGateWay) {
      if (m_ReloadConfig.IamClient)
        m_ReloadConfig.StartStoreMobile = DateTime2.Now;
      else
        m_ReloadConfig.StartStore = DateTime.Now;

      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;
      ResourceId res_id = new ResourceId(ResourceName);
      //List<StoreKindData> kind_data = new List<StoreKindData>();


      Node node = null;

      if (viaGateWay != null) {
        //NodeId gateway = new ResourceId(viaGateWay);

        node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1} via Gateway {2}", ResourceName, res_id, viaGateWay));

        if (m_ReloadConfig.IamClient && node == null) {
          node = m_ReloadConfig.AdmittingPeer;
        }

        foreach (StoreKindData storeKindData in kind_data) {
          if (node == null || node.Id == m_ReloadConfig.LocalNodeID) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Local storage at NodeId {0}", node.Id));
            m_topology.Store(res_id, storeKindData);
            yield break;
          }
        }

        Destination gateway = new Destination(new NodeId(viaGateWay.Data));
        Destination storeDestination = new Destination(res_id);
        StoreReq storeRequest = new StoreReq(storeDestination.destination_data.ressource_id,
                                              kind_data,
                                              m_machine.UsageManager);
        reloadSendMsg = create_reload_message(gateway, ++m_ReloadConfig.TransactionID, storeRequest);
        reloadSendMsg.forwarding_header.destination_list.Add(storeDestination);  //this is the real destination

        if (reloadSendMsg.AddDestinationOverlay(ResourceName))
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AddDestinationOverlay successful");
      }
      else {
        res_id = new ResourceId(ResourceName);

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1}", ResourceName, res_id));
        node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

        if (m_ReloadConfig.IamClient && node == null) {
          node = m_ReloadConfig.AdmittingPeer;
        }
        if (node == null || node.Id == m_ReloadConfig.LocalNodeID) {
          foreach (StoreKindData storeKindData in kind_data) {

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
              String.Format("Local storage at NodeId {0}", node.Id));
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
              "Store:0,011111");
            m_topology.Store(res_id, storeKindData);
          }
          if (storeDone != null) storeDone.Post(reloadDialog);
          yield break;
        }
        reloadSendMsg = create_store_req(new Destination(res_id), kind_data);
      }

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue,
              new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
                  new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          storeDone.Post(reloadDialog);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
          ReloadGlobals.PrintException(m_ReloadConfig, ex);
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;


        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

          if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
              String.Format("Store successful:{0} <== {1} TransId={2:x16}",
              reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

            m_ReloadConfig.EndStore = DateTime.Now;

            TimeSpan storeSpan = storeSpan = m_ReloadConfig.EndStore - m_ReloadConfig.StartStore;

            if (m_ReloadConfig.IamClient)
              storeSpan = DateTime2.Now - m_ReloadConfig.StartStoreMobile;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                                  String.Format("Store:{0}",
                                  storeSpan.TotalSeconds.ToString()));

            if (storeDone != null) storeDone.Post(reloadDialog);
          }
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
            String.Format("Store failed"));
          m_statistics.IncTransmissionError();
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store 2: " + ex.Message);
        ReloadGlobals.PrintException(m_ReloadConfig, ex);
      }

      if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Shutdown)
        m_machine.Finish();

    }

    /// <summary>
    /// Proprietary: Tries to fetch the Data specified by the StoredDataSpecifier List from different RELOAD overlay using viaGateWay as gateway
    /// </summary>
    /// <param name="resourceName"></param>
    /// <param name="specifiers"></param>
    /// <param name="viaGateWay"></param>
    /// <returns></returns>
    public IEnumerator<ITask> Fetch(string resourceName, List<StoredDataSpecifier> specifiers, NodeId viaGateWay) {
      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;
      List<IUsage> recUsages = new List<IUsage>();
      ResourceId res_id = new ResourceId(resourceName);

      Node node = null;
      List<FetchKindResponse> fetchKindResponses = new List<FetchKindResponse>();
      FetchKindResponse fetchKindResponse = null;

      if (viaGateWay != null) {

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Fetch {0} as ResID: {1} via Gateway {2}", resourceName, res_id, viaGateWay));

        node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);
        //node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

        if (m_ReloadConfig.IamClient && node == null) {
          node = m_ReloadConfig.AdmittingPeer;
        }

        Destination gateway = new Destination(new NodeId(viaGateWay.Data));
        Destination fetchDestination = new Destination(res_id);
        //reloadSendMsg = create_fetch_req(gateway, specifiers);
        FetchReq fetchRequest = new FetchReq(fetchDestination.destination_data.ressource_id,
                                              specifiers,
                                              m_machine.UsageManager);

        reloadSendMsg = create_reload_message(gateway, ++m_ReloadConfig.TransactionID, fetchRequest);
        reloadSendMsg.forwarding_header.destination_list.Add(fetchDestination);

        //reloadSendMsg = create_fetch_req(new Destination(res_id), specifiers);
        //reloadSendMsg.AddViaHeader(viaGateWay);

        if (reloadSendMsg.AddDestinationOverlay(resourceName))
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AddDestinationOverlay successful");
      }
      else {

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
          String.Format("Fetch {0} as ResID: {1}", resourceName, res_id));

        node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

        if (m_ReloadConfig.IamClient && node == null) {
          node = m_ReloadConfig.AdmittingPeer;
        }

        List<Destination> dest_list = new List<Destination>();
        dest_list.Add(new Destination(m_topology.LocalNode.Id));

        if (node == null || node.Id == m_ReloadConfig.LocalNodeID) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Local Fetch.");
          m_ReloadConfig.StartFetchAttach = DateTime.Now;
          foreach (StoredDataSpecifier specifier in specifiers) {
            var responses = new List<FetchKindResponse>();
            if (m_topology.Fetch(res_id, specifier, out fetchKindResponse)) {
              responses.Add(fetchKindResponse);
              foreach (StoredData sd in fetchKindResponse.values) {
                if (m_ReloadConfig.AccessController.validateDataSignature(res_id, fetchKindResponse.kind, sd))
                  recUsages.Add(sd.Value.GetUsageValue);
                else {
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!!");
                }
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                    String.Format("Fetch successful, got {0}",
                        sd.Value.GetUsageValue.Report()));
              }
              m_ReloadConfig.ConnEstEnd = DateTime.Now;
            }
            OnFetchedData(res_id, responses);
          }


          if (fetchDone != null) {
            if (recUsages.Count == 0) {
              foreach (StoredDataSpecifier specifier in specifiers)
                recUsages.Add(new NoResultUsage(specifier.ResourceName));
            }
            fetchDone.Post(recUsages);
          }
          yield break;
        }
        else {
          reloadSendMsg = create_fetch_req(new Destination(res_id), specifiers);
        }
      }

      int RetransmissionTime = ReloadGlobals.RetransmissionTime +
        ReloadGlobals.MaxTimeToSendPacket;

      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 &&
        m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().
              PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));
          m_ReloadConfig.StartFetchAttach = DateTime.Now;
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
            reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
            RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          fetchDone.Post(new List<IUsage> { new NullUsage() });
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "Fetch: " + ex.Message);
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;


        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
          RELOAD_MessageCode recMsgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
          if (recMsgCode == RELOAD_MessageCode.Fetch_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} <== {1} TransId={2:x16}",
                recMsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                  reloadRcvMsg.TransactionID));
            FetchAns answ = (FetchAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              fetchKindResponses = answ.KindResponses;
              foreach (FetchKindResponse kind in fetchKindResponses) {
                foreach (StoredData sd in kind.values) {
                  if (m_ReloadConfig.AccessController.validateDataSignature(res_id, kind.kind, sd))
                    recUsages.Add(sd.Value.GetUsageValue);
                  else {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!!");
                  }
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                    String.Format("Fetch successful, got {0}",
                      sd.Value.GetUsageValue.Report()));
                }
              }
              OnFetchedData(res_id, fetchKindResponses);
              if (fetchDone != null) {
                if (recUsages.Count == 0) {
                  foreach (StoredDataSpecifier specifier in specifiers)
                    recUsages.Add(new NoResultUsage(specifier.ResourceName));
                }
                fetchDone.Post(recUsages);
              }
            }
          }
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Fetch failed"));
          foreach (StoredDataSpecifier specifier in specifiers)
            recUsages.Add(new NoResultUsage(specifier.ResourceName));
          fetchDone.Post(recUsages);
          m_statistics.IncTransmissionError();
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Fetch: " + ex.Message);
      }
    }

    /// <summary>
    /// Proprietary: Starts an AppAttachProcedure which is routed through the viaGateWay Node into the "overlayName" Overlay 
    /// </summary>
    /// <param name="dest">Destination to appattach to </param>
    /// <param name="viaGateWay">Node Id of the GateWay into interconnection Overlay</param>
    /// <param name="overlayName">Name of the Overlay the other Peer is participating. Needed by the Gateway for further routing.</param>
    /// <returns></returns>
    public IEnumerator<ITask> AppAttachProcedure(Destination dest, NodeId viaGateWay, string overlayName) {
      ReloadMessage reloadSendMsg;
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure to {0} via GateWay {1}", dest, viaGateWay));
      reloadSendMsg = create_app_attach_req(new Destination(new NodeId(viaGateWay.Data)));
      reloadSendMsg.forwarding_header.destination_list.Add(dest);

      if (reloadSendMsg.AddDestinationOverlay(overlayName))
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AddDestinationOverlay successful");

      //create_reload_message(new ReloadMessage(m_ReloadConfig, m_topology.LocalNode.Id, dest, ++m_ReloadConfig.TransactionID, new AppAttachReqAns(m_topology.LocalNode, true)));

      if (dest.type != DestinationType.node) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure failed: unexpected destination type"));
        yield break;
      }

      if (dest.destination_data.node_id == m_topology.Id) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("local AppAttachProcedure dropped"));
        yield break;
      }

      //Node node = m_topology.routing_table.FindNextHopTo(dest.destination_data.node_id, true, false);
      Node node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);

      if (node == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure: failed, did not found next hop to {0}", dest.destination_data.node_id));
        yield break;
      }

      ReloadDialog reloadDialog = null;

      int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
      int iRetrans = ReloadGlobals.MaxRetransmissions;

      m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.attaching);

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} via {1} ==> Dest={2} TransId={3:x16}",
                                                                                  RELOAD_MessageCode.App_Attach_Request.ToString().PadRight(16, ' '),
                                                                                  node,
                                                                                  dest.ToString(),
                                                                                  reloadSendMsg.TransactionID));

          Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });


        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

          if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.App_Attach_Answer) {
            AppAttachReqAns answ = (AppAttachReqAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              node = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);
              /*  An Attach in and of itself does not result in updating the routing
               *  table of either node.
               *  Note: We use the routing table here only for storing ice candidates 
               *  for later use, we will not update successor or predecessor list
               */
              m_topology.routing_table.AddNode(node);
              if (m_topology.routing_table.GetNodeState(node.Id) < NodeState.attached)
                m_topology.routing_table.SetNodeState(node.Id, NodeState.attached);

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} last={2} TransId={3:x16}",
                  reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.LastHopNodeId, reloadRcvMsg.TransactionID));

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("AppAttach returns IP: {0}", answ.ice_candidates[0].addr_port.ipaddr.ToString()));
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("AppAttach returns IP: {0}:{1}", answ.ice_candidates[0].addr_port.ipaddr.ToString(), answ.ice_candidates[0].addr_port.port.ToString()));
              /* Proprietary registry interface function to support external clients */
              ReloadGlobals.StoreRegAnswer("sip:" + answ.ice_candidates[0].addr_port.ipaddr.ToString() + ":5060");
              m_ReloadConfig.ConnEstEnd = DateTime.Now;
            }
          }
        }
        else {
          m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
        m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
      }
    }

    #endregion

    /// <summary>
    /// Just the leaving Procedure
    /// </summary>
    /// <returns></returns>
    public IEnumerator<ITask> Leave() {
      m_ReloadConfig.State = ReloadConfig.RELOAD_State.Leave;

      foreach (ReloadConnectionTableInfoElement rce in m_flm.ConnectionTable) {
        ReloadDialog reloadDialog = null;
        ReloadMessage reloadSendMsg = create_leave_req(new Destination(rce.NodeID));

        int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

        int iRetrans = ReloadGlobals.MaxRetransmissions;

        while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
          try {
            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm,
              m_topology.routing_table.GetNode(rce.NodeID));

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              rce.NodeID, reloadSendMsg.TransactionID));

            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Leave: " + ex.Message);
          }

          yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

          if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
            break;


          /* If a response has not been received when the timer fires, the request
             is retransmitted with the same transaction identifier. 
          */
          --iRetrans;
        }

        try {
          if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
            /*the SourceNodeID delivered from reloadDialog comes from
             * connection table and is the last hop of the message
             */
            ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
            RELOAD_MessageCode code = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
            if (code == RELOAD_MessageCode.Leave_Answer) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                String.Format("{0} <== {1} TransId={2:x16}",
                code.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                reloadRcvMsg.TransactionID));
            }
          }
          else {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
              String.Format("Leave failed"));
            m_statistics.IncTransmissionError();
          }
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "Send Leave: " + ex.Message);
        }
      }

      // Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, Node>(reloadSendMsg, m_topology.routing_table.GetNode(rce.NodeID), Send));
      m_ReloadConfig.State = ReloadConfig.RELOAD_State.PreJoin;
      m_machine.StateUpdates(ReloadConfig.RELOAD_State.PreJoin);
    }

    /// <summary>
    /// Handover key if: 1. leave overlay 2. I'm AP while a join req happens.
    /// </summary>
    /// <param name="fSendLeaveFirst"></param>
    /// <returns></returns>
    public IEnumerator<ITask> HandoverKeys(bool fSendLeaveFirst) {
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Handover Keys!");

      if (fSendLeaveFirst) {
        Leave();
      }

      // For each Resource stored at this Peer, handover StoredData
      List<string> storedKeys;
      if ((storedKeys = m_topology.Storage.StoredKeys) != null && storedKeys.Count > 0) {

        m_topology.Storage.RemoveExpired();

        Dictionary<ResourceId, List<StoreKindData>> nodes = new Dictionary<ResourceId, List<StoreKindData>>();

        Dictionary<ResourceId, Node> destinations = new Dictionary<ResourceId, Node>();

        foreach (string key in storedKeys) {
          ResourceId res_id = new ResourceId(ReloadGlobals.HexToBytes(key));
          Node currentNode = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, fSendLeaveFirst);
          if (currentNode == null || currentNode.Id == m_ReloadConfig.LocalNodeID) {
            //everything's fine, key still belongs to me
            continue;
          }
          else {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Handover Keys - will send store requests");
            if (!nodes.ContainsKey(res_id)) {
              nodes.Add(res_id, new List<StoreKindData>());
              nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));

              destinations.Add(res_id, currentNode);
            }
            else {
              nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));
            }
          }
        }

        ReloadDialog reloadDialog = null;
        ReloadMessage reloadSendMsg;
        List<StoreKindData> storeKindData;

        foreach (ResourceId res_id in nodes.Keys) {
          Node node = destinations[res_id];
          storeKindData = nodes[res_id];

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "GOING TO STORE UNDER RES_ID: " + res_id);
          foreach (StoreKindData skd in storeKindData)
            foreach (StoredData sd in skd.Values)
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "STOREDATA: " + sd.Value.GetUsageValue.Report());
          reloadSendMsg = create_store_req(new Destination(res_id), storeKindData);

          int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

          int iRetrans = ReloadGlobals.MaxRetransmissions;

          while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
            try {
              reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                  reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

              Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
            }
            catch (Exception ex) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
            }

            yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

            if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
              break;


            /* If a response has not been received when the timer fires, the request
               is retransmitted with the same transaction identifier. 
            */
            --iRetrans;
          }

          try {
            if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
              ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

              if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

                //StoreReqAns answ = (StoreReqAns)reloadRcvMsg.reload_message_body; --old
                StoreAns answ = (StoreAns)reloadRcvMsg.reload_message_body; // --alex

                if (answ != null) {
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Delete Key {0}", res_id));
                  // m_topology.StoredValues.Remove(StoredKey); --old
                  m_topology.Storage.Remove(res_id.ToString());
                }
              }
            }
            else {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Store failed"));
              m_statistics.IncTransmissionError();
            }
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
          }
        }
      }
      if (fSendLeaveFirst)
        //this will reset neighbor tables
        m_topology.Leave();
    }

    /// <summary>
    /// The Fetch request retrieves one or more data elements stored at a
    /// given Resource-ID.  A single Fetch request can retrieve multiple
    /// different kinds.
    /// 
    /// RELOAD base -13 p.92
    /// --alex
    /// </summary>
    /// <param name="resourceName">The resouces name (human readable)</param>
    /// <param name="specifiers">StoredSpecifier objects</param>
    /// <returns></returns>
    public IEnumerator<ITask> Fetch(string resourceName, List<StoredDataSpecifier> specifiers) {
      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;
      List<IUsage> recUsages = new List<IUsage>();
      ResourceId res_id = new ResourceId(resourceName);

      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          String.Format("Fetch {0} as ResID: {1}", resourceName, res_id));

      Node node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

      if (m_ReloadConfig.IamClient && node == null) {
        node = m_ReloadConfig.AdmittingPeer;
      }

      List<Destination> dest_list = new List<Destination>();
      dest_list.Add(new Destination(m_topology.LocalNode.Id));
      List<FetchKindResponse> fetchKindResponses = new List<FetchKindResponse>();
      FetchKindResponse fetchKindResponse = null;

      if (node == null || node.Id == m_ReloadConfig.LocalNodeID) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Local Fetch.");
        m_ReloadConfig.StartFetchAttach = DateTime.Now;
        foreach (StoredDataSpecifier specifier in specifiers) {
          var responses = new List<FetchKindResponse>();
          if (m_topology.Fetch(res_id, specifier, out fetchKindResponse)) {
            responses.Add(fetchKindResponse);
            foreach (StoredData sd in fetchKindResponse.values) {
              if (m_ReloadConfig.AccessController.validateDataSignature(res_id, fetchKindResponse.kind, sd))
                recUsages.Add(sd.Value.GetUsageValue);
              else {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "DATA SIGNATURE INVALID!!");
              }
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                  String.Format("Fetch successful, got {0}",
                      sd.Value.GetUsageValue.Report()));
            }
            m_ReloadConfig.ConnEstEnd = DateTime.Now;
          }
          OnFetchedData(res_id, responses);
        }


        if (fetchDone != null) {
          if (recUsages.Count == 0) {
            foreach (StoredDataSpecifier specifier in specifiers)
              recUsages.Add(new NoResultUsage(specifier.ResourceName));
          }
          fetchDone.Post(recUsages);
        }
        yield break;
      }
      else {
        reloadSendMsg = create_fetch_req(new Destination(res_id), specifiers);
      }

      int RetransmissionTime = ReloadGlobals.RetransmissionTime +
        ReloadGlobals.MaxTimeToSendPacket;

      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 &&
        m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("{0} ==> {1} TransId={2:x16}",
              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().
              PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));
          m_ReloadConfig.StartFetchAttach = DateTime.Now;
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
            reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
            RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          fetchDone.Post(new List<IUsage> { new NullUsage() });
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "Fetch: " + ex.Message);
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;


        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
          RELOAD_MessageCode recMsgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
          if (recMsgCode == RELOAD_MessageCode.Fetch_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} <== {1} TransId={2:x16}",
                recMsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                  reloadRcvMsg.TransactionID));
            FetchAns answ = (FetchAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              fetchKindResponses = answ.KindResponses;
              foreach (FetchKindResponse kind in fetchKindResponses) {
                foreach (StoredData sd in kind.values) {
               //   if (m_ReloadConfig.AccessController.validateDataSignature(res_id, kind.kind, sd)) TODO:
                    recUsages.Add(sd.Value.GetUsageValue);
               //   else {
              //      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!!");
               //   }
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                    String.Format("Fetch successful, got {0}",
                      sd.Value.GetUsageValue.Report()));
                }
              }
              OnFetchedData(res_id, fetchKindResponses);
              if (fetchDone != null) {
                if (recUsages.Count == 0) {
                  foreach (StoredDataSpecifier specifier in specifiers)
                    recUsages.Add(new NoResultUsage(specifier.ResourceName));
                }
                fetchDone.Post(recUsages);
              }
            }
          }
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Fetch failed"));
          foreach (StoredDataSpecifier specifier in specifiers)
            recUsages.Add(new NoResultUsage(specifier.ResourceName));
          fetchDone.Post(recUsages);
          m_statistics.IncTransmissionError();
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Fetch: " + ex.Message);
      }
    }


    /// <summary>
    /// On data fetch execute the Usages AppProcedure
    /// </summary>
    /// <param name="res_id"></param>
    /// <param name="fetchKindResponse"></param>
    private void OnFetchedData(ResourceId res_id,
      List<FetchKindResponse> fetchKindResponses) {
      foreach (var fetchKindResponse in fetchKindResponses) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
          String.Format("Fetch on {0} returns {1}",
            res_id, fetchKindResponse.ToString()));
      }

      m_machine.UsageManager.AppProcedure(fetchKindResponses);

    }

    public IEnumerator<ITask> Fetch() {
      throw new NotImplementedException();
    }

    public IEnumerator<ITask> SendUpdate(Node node, Node nexthopnode) {
      ReloadDialog reloadDialog = null;
      ReloadMessage reloadSendMsg;

      /*if (m_topology.routing_table.isFinger(node.Id))
        m_topology.routing_table.AddFinger(node);*/

      if (nexthopnode == null)
        nexthopnode = node;

      Destination dest = new Destination(node.Id);

      reloadSendMsg = create_update_req(dest, m_topology.routing_table,
        ChordUpdateType.neighbors);

      int RetransmissionTime = ReloadGlobals.RetransmissionTime +
         ReloadGlobals.MaxTimeToSendPacket;
      int iRetrans = ReloadGlobals.MaxRetransmissions;

      while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        try {
          reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, nexthopnode);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.Update_Request.ToString().PadRight(16, ' '),
            node.Id, reloadSendMsg.TransactionID));
          //m_ReloadConfig.start
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
            reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
            RetransmissionTime, reloadDialog.Execute));
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "Send Update: " + ex.Message);
          yield break;
        }

        yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
          break;

        /* If a response has not been received when the timer fires, the request
           is retransmitted with the same transaction identifier. 
        */
        --iRetrans;
        if (iRetrans > 0) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
            String.Format("Retrans {0} SendUpdate  {1}:{2} TransId={3:x16}",
            iRetrans, node, nexthopnode, reloadSendMsg.TransactionID));
          m_statistics.IncRetransmission();
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("Failed! SendUpdate  {0}:{1} TransId={2:x16}",
            node, nexthopnode, reloadSendMsg.TransactionID));
          m_statistics.IncTransmissionError();
        }
      }

      try {
        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null) {
          //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
          ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
          RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
          if (msgCode == RELOAD_MessageCode.Update_Answer) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} <== {1} TransId={2:x16}",
              msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
              reloadRcvMsg.TransactionID));

            UpdateReqAns answ = (UpdateReqAns)reloadRcvMsg.reload_message_body;

            if (answ != null) {
              NodeId originator = reloadRcvMsg.OriginatorID;

              if (m_topology.routing_table.FingerSuccessors.Contains(originator)) {
                //m_topology.routing_table.GetNode(originator).Successors = answ.Successors;
                //m_topology.routing_table.GetNode(originator).Predecessors = answ.Predecessors;
                m_topology.routing_table.SetFingerState(originator,
                  NodeState.updates_received);
              }
              if (m_topology.routing_table.RtTable.ContainsKey(originator.ToString())) {
                m_topology.routing_table.SetNodeState(originator,
                  NodeState.updates_received);
                m_topology.routing_table.GetNode(originator).Successors = answ.Successors;
                m_topology.routing_table.GetNode(originator).Predecessors = answ.Predecessors;
              }
            }
          }
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          "Send Update: " + ex.Message);
      }
    }

    public void InboundClose(NodeId nodeid) {
      Boolean important_node = m_topology.routing_table.NodeWeNeed(nodeid);

      if (important_node)
        Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(nodeid), PingOption.direct, SendPing));
    }

    public Node NextHopToDestination(Destination dest, ref bool direct) {
      Node NextHopNode = null;
      direct = false;

      if (dest.type == DestinationType.node) {
        NextHopNode = m_topology.routing_table.GetNode(dest.destination_data.node_id);

        if (NextHopNode == null)
          /*  The Topology Plugin is responsible for maintaining the overlay
              algorithm Routing Table, which is consulted by the Forwarding and
              Link Management Layer before routing a message. 
           */
          NextHopNode = m_topology.routing_table.FindNextHopTo(
            dest.destination_data.node_id, true, false);
        else
          direct = true;
      }
      else {
        NextHopNode = m_topology.routing_table.FindNextHopTo(new NodeId(
          dest.destination_data.ressource_id), true, false);
      }

      if (NextHopNode == null)
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          "Did not found next hop to: " + dest);
      return NextHopNode;
    }

    public IEnumerator<ITask> Send(ReloadMessage reloadSendMsg, Node NextHopNode) {
      if (reloadSendMsg.reload_message_body.RELOAD_MsgCode ==
        RELOAD_MessageCode.Error)
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          String.Format("{0} ==> {1} code={2}: msg=\"{3}\", dest={4}",
          reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
          NextHopNode, ((ErrorResponse)reloadSendMsg.reload_message_body).ErrorCode,
          ((ErrorResponse)reloadSendMsg.reload_message_body).ErrorMsg,
          reloadSendMsg.forwarding_header.destination_list[0]));
      try {
        //GetForwardingAndLinkManagementLayer().Send(NextHopNode, reloadSendMsg);
        Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, ReloadMessage>(NextHopNode, reloadSendMsg, GetForwardingAndLinkManagementLayer().Send));
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          "SendAnswer: " + ex.Message);
      }
      yield break;
    }

    public Boolean CheckAndSetAdmittingPeer(Node node) {
      if (!m_ReloadConfig.IsBootstrap)
        if ((m_ReloadConfig.AdmittingPeer == null ||
             node.Id.ElementOfInterval(m_topology.Id,
             m_ReloadConfig.AdmittingPeer.Id, true)) &&
             !(m_ReloadConfig.AdmittingPeer != null &&
             m_ReloadConfig.AdmittingPeer.Id == node.Id)) {
          m_ReloadConfig.AdmittingPeer = node;
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("AttachProcedure: Successfully attached to new" +
              "admitting peer {0}", m_ReloadConfig.AdmittingPeer.Id));
          return true;
        }

      return false;
    }

    public void reload_attach_inbound(ReloadMessage recmsg) {
      AttachReqAns req_answ = (AttachReqAns)recmsg.reload_message_body;
      NodeId OriginatorID = recmsg.OriginatorID;

      if (req_answ != null && req_answ.ice_candidates != null) {
        Node attacher = new Node(recmsg.OriginatorID, req_answ.ice_candidates);
        bool isFinger = m_topology.routing_table.isFinger(attacher.Id);

        m_topology.routing_table.AddNode(attacher);
        m_topology.routing_table.SetNodeState(recmsg.OriginatorID, NodeState.attached);

        if (recmsg.IsRequest()) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.Attach_Answer.ToString().PadRight(16, ' '),
            OriginatorID, recmsg.TransactionID));

          ReloadMessage sendmsg = create_attach_answ(
            new Destination(OriginatorID), recmsg.TransactionID);
          recmsg.PutViaListToDestination(sendmsg);
          //sendmsg.addOverlayForwardingOptions(recmsg);  //Proprietary  //--Joscha	
          if (m_machine is GWMachine) { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
            if (sendmsg.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id) {
              sendmsg.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
              ((GWMachine)m_machine).GateWay.interDomainPeer.Transport.receive_message(sendmsg);
            }
            else
              send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
          }
          else
            send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));

          if (req_answ.SendUpdate)
            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, Node>(
              m_topology.routing_table.GetNode(OriginatorID),
              m_topology.routing_table.GetNode(recmsg.LastHopNodeId),
              SendUpdate));
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("{0} <== {1} (not handled!!)",
            req_answ.RELOAD_MsgCode.ToString().PadRight(16, ' '), OriginatorID));
        }
      }
    }

    public void reload_app_attach_inbound(ReloadMessage recmsg) {
      AppAttachReqAns req_answ = (AppAttachReqAns)recmsg.reload_message_body;
      NodeId OriginatorID = recmsg.OriginatorID;
      Node Originator = new Node(recmsg.OriginatorID, req_answ.ice_candidates);

      m_topology.routing_table.AddNode(Originator);
      m_topology.routing_table.SetNodeState(Originator.Id, NodeState.attached);

      //Proprietary --joscha
      string destination_overlay = null;
      string source_overlay = null;

      if (recmsg.forwarding_header.fw_options != null) {

        foreach (ForwardingOption option in recmsg.forwarding_header.fw_options) {
          if (option.fwo_type == ForwardingOptionsType.destinationOverlay) {
            destination_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("{0} Message  for destinationOverlay=" + destination_overlay, recmsg.reload_message_body.RELOAD_MsgCode));
          }
          if (option.fwo_type == ForwardingOptionsType.sourceOverlay) {
            source_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Message from sourceOverlay=" + source_overlay));
          }
        }
      }

      if (destination_overlay == null)// --joscha Do not establish a connection to a different overlay    
        if (CheckAndSetAdmittingPeer(Originator) && Originator.Id != recmsg.LastHopNodeId)
          // Send ping to establish a physical connection
          Arbiter.Activate(m_DispatcherQueue,
            new IterativeTask<Destination, PingOption>(new Destination(Originator.Id),
            PingOption.direct, SendPing));
<<<<<<< HEAD
      if (source_overlay == m_ReloadConfig.OverlayName) {

=======
      if (source_overlay == ReloadGlobals.OverlayName)
      {
      
>>>>>>> c72920f5592677c84932e6ebf9afc0acefa648a4
        // Send ping to establish a physical connection
        Arbiter.Activate(m_DispatcherQueue,
          new IterativeTask<Destination, PingOption>(new Destination(Originator.Id),
          PingOption.direct, SendPing));
      }
      if (req_answ != null && req_answ.ice_candidates != null) {
        if (recmsg.IsRequest()) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
            String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.App_Attach_Answer.ToString().PadRight(16, ' '),
            OriginatorID, recmsg.TransactionID));

          ReloadMessage sendmsg = create_app_attach_answ(
            new Destination(OriginatorID), recmsg.TransactionID);
          recmsg.PutViaListToDestination(sendmsg);
          //sendmsg.addOverlayForwardingOptions(recmsg);  //--joscha
          send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("{0} <== {1} (not handled!!)", req_answ.RELOAD_MsgCode.ToString().PadRight(16, ' '), OriginatorID));
        }
      }
    }

    private void reload_join_inbound(ReloadMessage recmsg) {
      JoinReqAns req_answ = (JoinReqAns)recmsg.reload_message_body;
      NodeId OriginatorID = recmsg.OriginatorID;

      if (recmsg.IsRequest()) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.Join_Answer.ToString().PadRight(16, ' '), OriginatorID, recmsg.TransactionID));
        /* leaving Address and port empty, that should already be stored in link management tables */

        // 5.  JP MUST send a Join to AP.  The AP sends the response to the
        // Join. RELOAD base -13 .105
        ReloadMessage sendmsg = create_join_answ(new Destination(OriginatorID), recmsg.TransactionID);
        recmsg.PutViaListToDestination(sendmsg);
        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));

        /*  AP MUST send JP an Update explicitly labeling JP as its predecessor. */
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Send update on join"));
        Arbiter.Activate(m_DispatcherQueue,
          new IterativeTask<Node, Node>(
          m_topology.routing_table.GetNode(OriginatorID),
          m_topology.routing_table.GetNode(recmsg.LastHopNodeId), SendUpdate));

        if (m_ReloadConfig.IsBootstrap && m_ReloadConfig.State != ReloadConfig.RELOAD_State.Joined) {
          m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joined;
          m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joined);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
            String.Format("Changed to joined state"));
        }
      }
      else {
      }
    }

    private void reload_update_inbound(ReloadMessage recmsg) {
      UpdateReqAns req_answ = (UpdateReqAns)recmsg.reload_message_body;
      NodeId OriginatorID = recmsg.OriginatorID;
      Boolean force_send_update = false;

      if (recmsg.IsRequest()) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
          String.Format("{0} ==> {1} TransId={2:x16}",
          RELOAD_MessageCode.Update_Answer.ToString().PadRight(16, ' '),
          OriginatorID, recmsg.TransactionID));

        ReloadMessage sendmsg = create_update_answ(
          new Destination(OriginatorID), recmsg.TransactionID,
          RELOAD_ErrorCode.invalid);

        recmsg.PutViaListToDestination(sendmsg);
        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
        //NodeId originator = recmsg.OriginatorID;
        m_topology.routing_table.SetNodeState(OriginatorID,
          NodeState.updates_received);
        m_topology.routing_table.SetFingerState(OriginatorID,
          NodeState.updates_received);
        if (req_answ.Successors.Count > 0) {
          m_topology.routing_table.GetNode(OriginatorID).Successors = req_answ.Successors;
          m_topology.routing_table.GetNode(OriginatorID).Predecessors = req_answ.Predecessors;
        }

        if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joining) {
          if (m_ReloadConfig.AdmittingPeer != null &&
            OriginatorID == m_ReloadConfig.AdmittingPeer.Id) {
            if (!m_topology.routing_table.IsWaitForJoinAnsw(OriginatorID)) {
              //we received an update from admitting peer, now joining is complete
              m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joined;
              m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joined);
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                String.Format("Joining completed"));
              m_ReloadConfig.LastJoinedTime = DateTime.Now;
              force_send_update = true;
            }
          }
        }
        //inform topo about incoming update
        Arbiter.Activate(m_DispatcherQueue,
          new IterativeTask<NodeId, UpdateReqAns, Boolean>(
          OriginatorID, req_answ, force_send_update, m_topology.routing_table.Merge));
      }
      else {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Incoming UpdateAns");
      }
    }

    /// <summary>
    /// Handles incoming StoreReq messages.
    /// </summary>
    /// <param name="recmg">The received RELOAD message</param>
    private void reload_store_inbound(ReloadMessage recmsg) {

      if (recmsg.IsRequest()) {

        StoreReq storeRequest = (StoreReq)recmsg.reload_message_body;
        NodeId originatorID = recmsg.OriginatorID;
        List<StoreKindData> recStoreKindData;

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
          String.Format("{0} ==> {1} via: {3:x20} TransId={2:x16}",
          RELOAD_MessageCode.Store_Answer.ToString().PadRight(16, ' '),
          originatorID, recmsg.TransactionID, recmsg.LastHopNodeId));

        recStoreKindData = storeRequest.StoreKindData;
        foreach (StoreKindData store_kind in recStoreKindData) {
          m_topology.Store(storeRequest.ResourceId, store_kind);
        }
        /* It then sends a Store request to its successor in the neighbor
         * table and to that peer's successor.
         * 
         * see RELOAD base -12 p.104
         */
        List<NodeId> replicaNodes = new List<NodeId>();

        NodeId successor = m_topology.routing_table.GetApprovedSuccessor();
        BigInt nextSuccsessor = successor + 1; ;
        NodeId successorsSuccessor = m_topology.routing_table.FindNextHopTo(
          new NodeId(nextSuccsessor.Data), true, false).Id;
        replicaNodes.Add(successor);
        replicaNodes.Add(successorsSuccessor);

        // send StoreAns to originator
        ReloadMessage storeAnswer = create_store_answ(
          new Destination(originatorID), recmsg.TransactionID, recStoreKindData,
          replicaNodes);
        recmsg.PutViaListToDestination(storeAnswer);
        //storeAnswer.addOverlayForwardingOptions(recmsg);  //Proprietary  //--Joscha	

        Node nextHop = m_topology.routing_table.GetNode(recmsg.LastHopNodeId);
        if (m_machine is GWMachine) { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
          if (storeAnswer.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id) {
            storeAnswer.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
            ((GWMachine)m_machine).GateWay.interDomainPeer.Transport.receive_message(storeAnswer);
          }
          else
            send(storeAnswer, nextHop);
        }
        else
          send(storeAnswer, nextHop);

#if false
                if (storeRequest.Replica_number < 2) {
                    ReloadMessage storeReplica1, storeReplica2;
                    // 1st Replica
                    if (successor != null) {                        
                        if (successor != m_ReloadConfig.LocalNodeID) {
                            storeReplica1 = create_store_req(new Destination(successor), recStoreKindData);
                            ((StoreReq)storeReplica1.reload_message_body).incrementReplicaNumber();
                            send(storeReplica1, m_topology.routing_table.GetNode(successor));
                        }
                    }
                    // 2nd Replica
                    if (successorsSuccessor != null) {
                        if (successorsSuccessor != m_ReloadConfig.LocalNodeID) {
                            storeReplica2 = create_store_req(new Destination(successorsSuccessor), recStoreKindData);
                            ((StoreReq)storeReplica2.reload_message_body).incrementReplicaNumber();
                            ((StoreReq)storeReplica2.reload_message_body).incrementReplicaNumber();
                            send(storeReplica2, m_topology.routing_table.GetNode(successorsSuccessor));
                        }
                    }
                } 
#endif
      }
      // its a StoreAns
      else {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Received StoreAns from: {0}", recmsg.OriginatorID));
        /*  ...this allows the storing peer to independently verify that the replicas have in fact been stored.
         *  Note that the storing peer is not require to perform this verification.
         *  see RELOAD base -12 p.91
         */
      }

    }

    /// <summary>
    /// Handles incoming fetch requests
    /// </summary>
    /// <param name="recmsg"></param>
    private void reload_fetch_inbound(ReloadMessage recmsg) {

      ReloadMessage sendmsg;

      if (recmsg.IsRequest()) {
        FetchReq fetchRequest = (FetchReq)recmsg.reload_message_body;
        NodeId originatorId = recmsg.OriginatorID;

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
          String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.Fetch_Answer.ToString().PadRight(16, ' '),
              originatorId, recmsg.TransactionID));

        var fetchKindResponses = new List<FetchKindResponse>();
        foreach (StoredDataSpecifier specifier in fetchRequest.Specifiers) {
          FetchKindResponse fetchKindResponse;
          if (m_topology.Fetch(fetchRequest.ResourceId,
              specifier, out fetchKindResponse)) {
            fetchKindResponses.Add(fetchKindResponse);
          }
          else {
            sendmsg = create_erro_reply(new Destination(originatorId),
              RELOAD_ErrorCode.Error_Not_Found,
                "Topology: RessourceId not found", recmsg.TransactionID);
            recmsg.PutViaListToDestination(sendmsg);
            send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));

            return;
          }
        }

        sendmsg = create_fetch_answ(new Destination(originatorId), recmsg.TransactionID, fetchKindResponses);
        recmsg.PutViaListToDestination(sendmsg);
        //sendmsg.addOverlayForwardingOptions(recmsg);  //Proprietary  //--Joscha	

        if (m_machine is GWMachine) { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
          if (sendmsg.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id) {
            sendmsg.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
            ((GWMachine)m_machine).GateWay.interDomainPeer.Inject(sendmsg); //TODO: change other cases
          }
          else
            send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
        }
        else
          send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
      }
      else {
        // It is a FetchAns, no reply needed
      }
    }

    private void reload_ping_inbound(ReloadMessage recmsg) {
      PingReqAns req_answ = (PingReqAns)recmsg.reload_message_body;
      NodeId originatorID = recmsg.OriginatorID;

      if (recmsg.IsRequest()) {
        // is still my Finger?
        /*
        Topology.RoutingTable.FTableEntry ftEntry;
        if (m_topology.routing_table.isFinger(originatorID, out ftEntry)) {
          if (m_topology.routing_table.IsAttached(originatorID))
            Arbiter.Activate(m_DispatcherQueue,
                new IterativeTask<Topology.RoutingTable.FTableEntry, ReloadMessage>(
                ftEntry, recmsg, AttachFinger));
        }
         */

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
          String.Format("{0} ==> {1} TransId={2:x16}",
          RELOAD_MessageCode.Ping_Answer.ToString().PadRight(16, ' '),
          originatorID, recmsg.TransactionID));

        ReloadMessage sendmsg = create_ping_answ(new Destination(originatorID),
          recmsg.TransactionID);
        recmsg.PutViaListToDestination(sendmsg);
        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
      }
      else {
      }
    }

    private void reload_leave_inbound(ReloadMessage recmsg) {
      LeaveReqAns req_answ = (LeaveReqAns)recmsg.reload_message_body;
      NodeId OriginatorID = recmsg.OriginatorID;

      if (req_answ.RELOAD_MsgCode == RELOAD_MessageCode.Leave_Request) {
        m_topology.InboundLeave(req_answ.LeavingNode);
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
            RELOAD_MessageCode.Leave_Answer.ToString().PadRight(16, ' '), OriginatorID, recmsg.TransactionID));

        ReloadMessage sendmsg = create_leave_answ(new Destination(OriginatorID), recmsg.TransactionID);
        recmsg.PutViaListToDestination(sendmsg);
        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
      }
      else {
      }
    }

    public void receive_message(ReloadMessage reloadMsg) {
      if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Exit)
        return;

      try {
        if (reloadMsg == null) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message: reloadMsg = null!!");
          return;
        }

        if (reloadMsg.IsFragmented() && reloadMsg.IsSingleFragmentMessage() == false) {  // -- joscha
          ReloadMessage reassembledMsg = null;
          lock (fragmentedMessageBuffer) {
            reassembledMsg = reloadMsg.ReceiveFragmentedMessage(ref fragmentedMessageBuffer);
          }
          if (reassembledMsg == null) //not yet all fragments received => not reassembled
            return;
          else
            reloadMsg = reassembledMsg; //message reassembled => continue as usual
        }

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("!!! receive_message: !!! {0} from {1}", Enum.GetName(typeof(RELOAD_MessageCode), reloadMsg.reload_message_body.RELOAD_MsgCode), reloadMsg.OriginatorID));

<<<<<<< HEAD
=======
        if (reloadMsg.forwarding_header.fw_options != null) { //handle proprietary forwarding options destination_overlay and source_overlay --joscha
          string destination_overlay = null;
          string source_overlay = null;
          //---------------DEBUG
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format(" reloadMsg.forwarding_header.fw_options != null via_list"));
          if (reloadMsg.forwarding_header.via_list != null) {
            foreach (Destination destx in reloadMsg.forwarding_header.via_list)
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
          }
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format(" reloadMsg.forwarding_header.fw_options != null destination_list"));
          if (reloadMsg.forwarding_header.destination_list != null) {
            foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
          }
          //---------------DEBUG
          foreach (ForwardingOption option in reloadMsg.forwarding_header.fw_options) {
            if (option.fwo_type == ForwardingOptionsType.destinationOverlay) {
              destination_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);
            }
            if (option.fwo_type == ForwardingOptionsType.sourceOverlay) {
              source_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);
            }
          }
          if (source_overlay != null && destination_overlay != null) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format(ReloadGlobals.OverlayName + ": " + "Message from sourceOverlay: " + source_overlay + " for destinationOverlay: " + destination_overlay));

            if (m_machine is GWMachine) {
              GWMachine gw = (GWMachine)m_machine;
              //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message has reached the GatewayPeer " + m_machine.ReloadConfig.OverlayName));
              if (ReloadGlobals.OverlayName == destination_overlay)
              {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("forwarded Message is in destination_overlay " + destination_overlay));
              }
                //              else if (gw.GateWay.mainPeer.ReloadConfig.OverlayName == destination_overlay) {
                //if (reloadMsg.forwarding_header.destination_list[0].type == DestinationType.node &&
                //   reloadMsg.forwarding_header.destination_list[0].destination_data.node_id == gw.GateWay.intraDomainPeer.Topology.LocalNode.Id) {
                //  gw.GateWay.intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message received by GateWayPeer"));
                //  gw.GateWay.Receive(source_overlay, reloadMsg);
                //  return;
                //}
                //else {
                //  //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message is forwarded within interdomain Overlay"));
                //  gw.GateWay.intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message received by WRONG GateWayPeer but i can handle it so i remove the correct GW Peer from destination list")); //TODO: find better solution
                //  reloadMsg.RemoveFirstDestEntry();
                //  gw.GateWay.Receive(source_overlay, reloadMsg);
                //  return;
                //}

              else if (ReloadGlobals.OverlayName == destination_overlay)
              {
                gw.GateWay.intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message received by GateWayPeer"));
                gw.GateWay.Receive(source_overlay, reloadMsg);
                return;
              }
              else if (ReloadGlobals.OverlayName != destination_overlay)
              {
                //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message: source_overlay " + source_overlay + " destination_overlay " + destination_overlay));
                gw.GateWay.Send(source_overlay, destination_overlay, reloadMsg);
                return;
              }
              else
                return;
            }
            else if (ReloadGlobals.OverlayName == destination_overlay)
            { //TODO:
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("forwarded Message is in destination_overlay " + destination_overlay));
            }
            else if (ReloadGlobals.OverlayName == source_overlay)
            {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message with forwarding options needs to be forwarded to GateWay Peer. destination_overlay is" + destination_overlay));
              //---------------DEBUG
              if (reloadMsg.forwarding_header.via_list != null) {
                foreach (Destination destx in reloadMsg.forwarding_header.via_list)
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
              }
              if (reloadMsg.forwarding_header.destination_list != null) {
                foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
              }
              //---------------DEBUG
              //return;
            }
            else
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message is forwarded within interdomain Overlay"));
          }
        }
        
>>>>>>> c72920f5592677c84932e6ebf9afc0acefa648a4
        //is this a request to be forwarded?
        if (!m_forwarding.ProcessMsg(reloadMsg)) {
          /* First of all, validate message */
          if (!m_ReloadConfig.AccessController.RequestPermitted(reloadMsg)) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              "Transport.receive_message(): " + reloadMsg.reload_message_body.RELOAD_MsgCode.ToString() + " Request originator cannot be validated!");
          }
          else
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              "Transport.receive_message(): " + reloadMsg.reload_message_body.RELOAD_MsgCode.ToString() + " Message signature verified and certificate authenticated!");
          /* handle only incoming RELOAD requests here...,
           * all answers will be handled in the appropriate tasks
           */
          if (reloadMsg.IsRequest()) {
            //message for local node 
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadMsg.OriginatorID, reloadMsg.TransactionID));

            if (reloadMsg.forwarding_header.via_list != null) {
              foreach (Destination destx in reloadMsg.forwarding_header.via_list)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Via={0} ", destx.ToString()));
            }
            if (reloadMsg.forwarding_header.destination_list != null) {
              foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                if (desty.type != DestinationType.node || reloadMsg.forwarding_header.destination_list.Count > 1)
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Dest={0} ", desty.ToString()));
            }

            switch (reloadMsg.reload_message_body.RELOAD_MsgCode) {
              case RELOAD_MessageCode.Probe_Request:
              case RELOAD_MessageCode.Probe_Answer:
                break;
              case RELOAD_MessageCode.Attach_Request:
              case RELOAD_MessageCode.Attach_Answer:
                reload_attach_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Store_Request:
                reload_store_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Store_Answer:
                reload_store_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Fetch_Request:
                reload_fetch_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Fetch_Answer:
                reload_fetch_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Remove_Request:
              case RELOAD_MessageCode.Remove_Answer:
                break;
              case RELOAD_MessageCode.Find_Request:
              case RELOAD_MessageCode.Find_Answer:
                break;
              case RELOAD_MessageCode.Join_Request:
              case RELOAD_MessageCode.Join_Answer:
                reload_join_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Leave_Request:
              case RELOAD_MessageCode.Leave_Answer:
                reload_leave_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Update_Request:
              case RELOAD_MessageCode.Update_Answer:
                reload_update_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Route_Query_Request:
              case RELOAD_MessageCode.Route_Query_Answer:
                break;
              case RELOAD_MessageCode.Ping_Request:
              case RELOAD_MessageCode.Ping_Answer:
                reload_ping_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Stat_Request:
              case RELOAD_MessageCode.Stat_Answer:
                break;
              case RELOAD_MessageCode.App_Attach_Request:
              case RELOAD_MessageCode.App_Attach_Answer:
                reload_app_attach_inbound(reloadMsg);
                break;
              case RELOAD_MessageCode.Error: {
                  ErrorResponse errorResponse = (ErrorResponse)reloadMsg.reload_message_body;
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("<== Error {0} Msg: {1}", errorResponse.ErrorCode, errorResponse.ErrorMsg));
                }
                break;
              case RELOAD_MessageCode.Unused:
              case RELOAD_MessageCode.Unused2:
              default:
                throw new System.Exception(String.Format("Invalid RELOAD message type {0}", reloadMsg.reload_message_body.RELOAD_MsgCode));
            }
          }
          else {

            /* m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
              String.Format("{0} <== {1}", 
              reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              reloadMsg.OriginatorID));
             */
            /*
            switch (reloadMsg.reload_message_body.RELOAD_MsgCode) {
                case RELOAD_MessageCode.Attach_Answer:
                    reload_attach_inbound((AttachReqAns)reloadMsg.reload_message_body, reloadMsg.OriginatorID, reloadMsg.TransID(), reloadMsg.LastHopNodeId);
                    break;
                default:
                    throw new System.Exception(String.Format("Invalid RELOAD message type {0}", reloadMsg.reload_message_body.RELOAD_MsgCode));
            }*/
          }
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message: " + ex.Message);
        ReloadGlobals.PrintException(m_ReloadConfig, ex);
        //System.Diagnostics.Debugger.Break();
      }
    }

    #region Creates Messages

    private ReloadMessage create_reload_message(Destination destination,
      UInt64 trans_id, RELOAD_MessageBody reload_content) {
      return new ReloadMessage(m_ReloadConfig,
        m_topology.LocalNode.Id, destination, trans_id, reload_content);
    }

    public ReloadMessage create_reload_message(ReloadMessage reloadRequest) {
      try {
        reloadRequest.PutViaListToDestination();
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          "create_reload_message: " + ex.Message);
      }
      return reloadRequest;
    }

    /// <summary>
    /// Creates an Attach request
    /// </summary>
    /// <param name="destination">The destination of this attach</param>
    /// <param name="fForceSendUpdate">Flag ForceSendUpdate</param>
    /// <returns></returns>
    public ReloadMessage create_attach_req(Destination destination,
      Boolean fForceSendUpdate) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
          new AttachReqAns(m_topology.LocalNode, true, fForceSendUpdate));
    }

    public ReloadMessage create_app_attach_req(Destination destination) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new AppAttachReqAns(m_topology.LocalNode, true));
    }

    public ReloadMessage create_attach_answ(Destination destination, UInt64 trans_id) {
      //return create_reload_message(destination, trans_id, new AttachReqAns(m_topology.LocalNode, false, true));
      return create_reload_message(destination, trans_id,
        new AttachReqAns(m_topology.LocalNode, false, false));
    }

    public ReloadMessage create_app_attach_answ(Destination destination, UInt64 trans_id) {
      return create_reload_message(destination, trans_id, new AppAttachReqAns(m_topology.LocalNode, false));
    }

    public ReloadMessage create_join_req(Destination destination) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new JoinReqAns(m_topology.LocalNode, true));
    }

    public ReloadMessage create_join_answ(Destination destination, UInt64 trans_id) {
      return create_reload_message(destination, trans_id, new JoinReqAns(null, false));
    }

    public ReloadMessage create_update_req(Destination destination,
      TopologyPlugin.RoutingTable rt, ChordUpdateType type) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
        new UpdateReqAns(rt.GetApproved(rt.Successors), rt.GetApproved(rt.Predecessors),
          type, m_ReloadConfig.StartTime));
    }

    public ReloadMessage create_update_answ(Destination destination, UInt64 trans_id, RELOAD_ErrorCode result) {
      return create_reload_message(destination, trans_id, new UpdateReqAns(result));
    }

    public ReloadMessage create_leave_req(Destination destination) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new LeaveReqAns(m_topology.LocalNode, true));
    }

    public ReloadMessage create_leave_answ(Destination destination, UInt64 trans_id) {
      return create_reload_message(destination, trans_id, new LeaveReqAns(m_topology.LocalNode, false));
    }

    public ReloadMessage create_ping_req(Destination destination) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new PingReqAns(0, true));
    }

    public ReloadMessage create_ping_answ(Destination destination, UInt64 trans_id) {
      Random rand = new Random();
      return create_reload_message(destination, trans_id, new PingReqAns((UInt64)rand.Next(int.MinValue, int.MaxValue), false));
    }

    /// <summary>
    /// Creates a StoreReq according to RELOAD base -12 p.86
    /// </summary>
    /// <param name="destination">The store destination</param>
    /// <param name="usage">The Usage data to be stored</param>
    /// <returns>A complete RELOAD StoreReq message including all headers</returns>
    public ReloadMessage create_store_req(Destination destination, List<StoreKindData> stored_kind_data) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
                                   new StoreReq(destination.destination_data.ressource_id,
                                                stored_kind_data,
                                                m_machine.UsageManager));
    }

    /// <summary>
    /// Creates a StoreAns message accoriding to RELOAD base -12 p.90
    /// </summary>
    /// <param name="destination">The answer destination address</param>
    /// <param name="trans_id">The transaction ID corresponding to the StoreReq</param>
    /// <param name="usage">The Usage data corresponding to the StoreReq</param>
    /// <returns></returns>
    public ReloadMessage create_store_answ(Destination dest,
      UInt64 trans_id, List<StoreKindData> skd, List<NodeId> replicas) {
      return create_reload_message(dest, trans_id, new StoreAns(skd, replicas));
    }

    /// <summary>
    /// Creates a FetchReq using the given StoredDataSpecifiers
    /// </summary>
    /// <param name="destination">The destination</param>
    /// <param name="specifiers">The StoredDataSpecifiers</param>
    /// <returns>A FetchReq</returns>
    private ReloadMessage create_fetch_req(Destination destination, List<StoredDataSpecifier> specifiers) {
      return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
                                   new FetchReq(destination.destination_data.ressource_id,
                                                specifiers,
                                                m_machine.UsageManager));
    }

    /// <summary>
    /// Creates a FetchAns
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="p"></param>
    /// <param name="fetchKindResponses"></param>
    /// <returns></returns>
    private ReloadMessage create_fetch_answ(Destination destination, UInt64 trans_id, List<FetchKindResponse> fetchKindResponses) {
      return create_reload_message(destination, trans_id, new FetchAns(fetchKindResponses, m_machine.UsageManager));
    }

    public ReloadMessage create_erro_reply(Destination destination, RELOAD_ErrorCode error, string errmsg, UInt64 trans_id) {
      return create_reload_message(destination, trans_id, new ErrorResponse(error, errmsg));
    }

    #endregion

    internal void send(ReloadMessage reloadMsg, Node NextHopNode) {
      if (NextHopNode == null || NextHopNode.Id == null)
        throw new System.Exception("Node == null on send");
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("!!! send: !!! {0} to {1}", Enum.GetName(typeof(RELOAD_MessageCode), reloadMsg.reload_message_body.RELOAD_MsgCode), reloadMsg.forwarding_header.destination_list[0]));
      if (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
        Arbiter.Activate(m_DispatcherQueue,
          new IterativeTask<ReloadMessage, Node>(reloadMsg, NextHopNode, Send));
    }

  #endregion
  }
}
