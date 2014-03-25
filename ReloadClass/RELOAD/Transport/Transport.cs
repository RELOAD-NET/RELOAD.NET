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
using System.Threading;
using UPNP;
using TSystems.RELOAD.Util;

namespace TSystems.RELOAD.Transport
{
    #region Transport

    public class MessageTransport
    {

        #region Properties

        private Dictionary<UInt64, SortedDictionary<UInt32, MessageFragment>> fragmentedMessageBuffer = new Dictionary<ulong, SortedDictionary<uint, MessageFragment>>();

        private DispatcherQueue m_DispatcherQueue;
        private Machine m_machine;
        private TopologyPlugin m_topology;
        private ReloadConfig m_ReloadConfig = null;


        // markus
        private ThreadSafeDictionary<ulong, List<IceCandidate>> m_attachRequestCandidates = new ThreadSafeDictionary<ulong, List<IceCandidate>>();
        // end markus


        /// <summary>
        /// Notifies about store status
        /// </summary>
        private Port<ReloadDialog> storeDone;
        public Port<ReloadDialog> StoreDone
        {
            get { return storeDone; }
            set { storeDone = value; }
        }

        /// <summary>
        /// Notifies about fetch status
        /// </summary>
        private Port<List<IUsage>> fetchDone;
        public Port<List<IUsage>> FetchDone
        {
            get { return fetchDone; }
            set { if (value != null) fetchDone = value; }
        }

        /// <summary>
        /// Notifies about AppAttach status
        /// </summary>
        private Port<IceCandidate> appAttachDone;
        public Port<IceCandidate> AppAttachDone
        {
            get { return appAttachDone; }
            set { if (value != null) appAttachDone = value; }
        }

        private IForwardLinkManagement m_flm;
        public IForwardLinkManagement GetForwardingAndLinkManagementLayer()
        {
            return m_flm;
        }

        private ForwardingLayer m_forwarding;
        private Statistics m_statistics;

        #endregion

        public void Init(Machine machine)
        {
            m_machine = machine;
            m_topology = machine.Topology;
            m_forwarding = machine.Forwarding;
            m_flm = machine.Interface_flm;
            m_DispatcherQueue = machine.ReloadConfig.DispatcherQueue;
            m_ReloadConfig = machine.ReloadConfig;
            m_statistics = m_ReloadConfig.Statistics;
        }

        public ReloadFLMEventArgs rfm_ReloadFLMEventHandler(object sender, ReloadFLMEventArgs args)
        {
            if (args.Eventtype == ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK)
            {
                receive_message(args.Message);
            }
            return args;
        }

        public IEnumerator<ITask> SendPing(Destination dest, PingOption pingOption)
        {
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

            if (dest.type == DestinationType.node)
            {
                /* This code assumes, that a node we want to ping is already attached and added to the routing table */
                NodeState nodestate = m_topology.routing_table.GetNodeState(dest.destination_data.node_id);
                bool pinging = m_topology.routing_table.GetPing(dest.destination_data.node_id);

                if (((pingOption & PingOption.standard | PingOption.finger) != 0) && nodestate == NodeState.unknown && !pinging)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Ignoring redundant Ping for {0}", dest));
                    yield break;
                }
                m_topology.routing_table.SetPinging(dest.destination_data.node_id, true, false);
            }
            else if (!NextHopIsDestination && ((pingOption & PingOption.direct) != 0))
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Direct Ping for {0} ignored, no entry in routing table", dest));
                yield break;
            }


            /* Don't spend too much time on connectivity checks */
            int iRetrans = 3;

            while (iRetrans > 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                        reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), NextHopNode.Id, reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                }
                catch (Exception ex)
                {
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

            try
            {
                PingReqAns answ = null;

                if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Ping_Answer)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

                        answ = (PingReqAns)reloadRcvMsg.reload_message_body;

                        if (answ != null)
                        {
                            if ((pingOption & PingOption.finger) != 0)
                            {
                                foreach (Topology.TopologyPlugin.RoutingTable.FTableEntry fte in m_topology.routing_table.FingerTable)
                                {
                                    if (fte.Finger == dest.destination_data.ressource_id)
                                    {
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
                else
                {
                    if (dest.type == DestinationType.node)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Ping failed: removing node {0}", dest.destination_data.node_id));
                        m_topology.routing_table.Leave(dest.destination_data.node_id);
                    }
                    m_statistics.IncTransmissionError();
                }

                if (dest.type == DestinationType.node)
                    m_topology.routing_table.SetPinging(dest.destination_data.node_id, false, answ != null);
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Ping: " + ex.Message);
            }
        }

        public IEnumerator<ITask> AttachProcedure(Destination dest, NodeId NextHopId, AttachOption attach_option)
        {
            ReloadMessage reloadSendMsg;

            /* 9.5.1 
             *     When a peer needs to Attach to a new peer in its neighbor table, it
                   MUST source-route the Attach request through the peer from which it
                   learned the new peer's Node-ID.  Source-routing these requests allows
                   the overlay to recover from instability.
            */

            reloadSendMsg = create_attach_req(dest, (attach_option & AttachOption.forceupdate) != 0);


            if (dest.type == DestinationType.node)
            {
                NodeState nodestate = m_topology.routing_table.GetNodeState(dest.destination_data.node_id);

                if (nodestate == NodeState.attaching)
                {
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

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
                    /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} via {1} ==> Dest={2} TransId={3:x16}",
                                                                                            RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
                                                                                            NextHopNode,
                                                                                            dest.ToString(),
                                                                                            reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AttachProcedure: " + ex.Message);
                    yield break;
                }

                yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (dest.type == DestinationType.node)
                    {
                        if (reloadRcvMsg.OriginatorID != dest.destination_data.node_id)
                        {
                            // drop message and retransmit request
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Received answer from unexpected peer"));
                            reloadRcvMsg = null;
                        }
                    }
                    else if (dest.type == DestinationType.resource)
                    {
                        int suc = m_topology.routing_table.GetSuccessorCount(false);
                        int pre = m_topology.routing_table.GetPredecessorCount(false);

                        if (suc >= 2 && pre >= 2)
                        {
                            // check if resource is mapping to a node in my routing table
                            if (dest.destination_data.ressource_id.ElementOfInterval(
                                m_topology.routing_table.Predecessors[pre - 2],
                                m_topology.routing_table.Successors[suc - 1],
                                true)
                            )
                            {
                                if (reloadRcvMsg.OriginatorID < m_topology.routing_table.Predecessors[pre - 2] && reloadRcvMsg.OriginatorID > m_topology.routing_table.Successors[suc - 1])
                                {
                                    // drop message and retransmit request
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Received answer from unexpected peer"));
                                    reloadRcvMsg = null;
                                }
                            }
                        }
                    }

                    if (reloadRcvMsg != null)
                        break;
                }

                /* If a response has not been received when the timer fires, the request
                   is retransmitted with the same transaction identifier. 
                */
                --iRetrans;
                if (iRetrans >= 0)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} SendAttach  via {1} TransId={2:x16}", iRetrans, NextHopNode, reloadSendMsg.TransactionID));
                    m_ReloadConfig.Statistics.IncRetransmission();
                }
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! SendAttach  via {0} TransId={1:x16}", NextHopNode, reloadSendMsg.TransactionID));
                    m_ReloadConfig.Statistics.IncTransmissionError();

                    if (dest.destination_data.node_id != null)
                    {
                        //m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
                        m_topology.routing_table.Leave(dest.destination_data.node_id);
                    }
                }
            }

            try
            {
                if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    /*the SourceNodeID delivered from reloadDialog comes from connection
                     * table and is the last hop of the message
                     */
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                    RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;

                    AttachReqAns answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

                    if (msgCode == RELOAD_MessageCode.Attach_Answer)
                    {
                        /* TKTODO
                         * 1.  The response to a message sent to a specific Node-ID MUST have
                               been sent by that Node-ID.
                           2.  The response to a message sent to a Resource-Id MUST have been
                               sent by a Node-ID which is as close to or closer to the target
                               Resource-Id than any node in the requesting node's neighbor
                               table.
                        */
                        if (answ != null)
                        {

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
                                || (attach_option & AttachOption.sendping) != 0)
                            {
                                // Send ping to get a physical connection
                                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(Originator.Id), PingOption.direct, SendPing));
                            }
                        }
                    }
                    else if (msgCode == RELOAD_MessageCode.Error)
                    {
                        if (dest.type == DestinationType.node)
                        {
                            ErrorResponse error = (ErrorResponse)reloadRcvMsg.reload_message_body;

                            if (error != null)
                            {
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
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AttachProcedure: " + ex.Message);

                if (dest.destination_data.node_id != null)
                    m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
            }
        }

        public IEnumerator<ITask> AppAttachProcedure(Destination dest)
        {
            ReloadMessage reloadSendMsg;

            reloadSendMsg = create_app_attach_req(dest);

            if (dest.type != DestinationType.node)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure failed: unexpected destination type"));
                yield break;
            }

            if (dest.destination_data.node_id == m_topology.Id)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("local AppAttachProcedure dropped"));
                yield break;
            }

            Node node = m_topology.routing_table.FindNextHopTo(dest.destination_data.node_id, true, false);

            if (node == null)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure: failed, did not found next hop to {0}", dest.destination_data.node_id));
                yield break;
            }

            ReloadDialog reloadDialog = null;

            int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
            int iRetrans = ReloadGlobals.MaxRetransmissions;

            m_topology.routing_table.SetNodeState(dest.destination_data.node_id,
              NodeState.attaching);

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
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
                catch (Exception ex)
                {
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

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.App_Attach_Answer)
                    {
                        AppAttachReqAns answ = (AppAttachReqAns)reloadRcvMsg.reload_message_body;

                        if (answ != null)
                        {
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
                            if (ReloadGlobals.AutoExe)
                            {
                                TimeSpan appAttachTime = DateTime.Now - m_ReloadConfig.StartFetchAttach;
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, String.Format("Fetch:{0}", appAttachTime.TotalSeconds));
                            }
                        }
                    }
                    else if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Error)
                    {
                        // TODO
                    }
                }
                else
                {
                    m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
                }
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
                m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
            }
        }

        public IEnumerator<ITask> PreJoinProdecure(List<BootstrapServer> BootstrapServerList)
        {
            bool attached = false;
            ulong bsTransId = 0;

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
            for (int i = 0; i < succSize; i++)
            {
                //if (last_destination != null && last_destination == dest)
                if (last_destination != null && last_destination.Equals(dest))    // markus: we have to use Equals method
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
                  m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
                {
                    /* This is the first bootstrap contacting sequence if NextHopNode 
                     * is still zero, in any other case 
                     * use an attach to the node where we got the last answer from 
                     */
                    if (NextHopNode == null)
                    {
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

                        // markus: change cand_type to bootstrap
                        ice.cand_type = CandType.tcp_bootstrap;

                        ics.Add(ice);

                        NextHopNode = new Node(reloadRcvMsg == null ?
                          null : reloadRcvMsg.OriginatorID, ics);
                    }

                    try
                    {
                        /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
                        reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, NextHopNode);

                        if (iCycleBootstrap > 0)
                        {
                            // save transaction id from request to bootstrap
                            if (NextHopNode.IceCandidates[0].addr_port.ipaddr.ToString() == BootstrapServerList[iCycleBootstrap - 1].Host &&
                                NextHopNode.IceCandidates[0].addr_port.port == BootstrapServerList[iCycleBootstrap - 1].Port)
                                bsTransId = reloadSendMsg.TransactionID;
                        }

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                          String.Format("{0} ==> {1} Dest={2} TransId={3:x16}",
                          RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
                          NextHopNode, dest.ToString(), reloadSendMsg.TransactionID));

                        Arbiter.Activate(m_DispatcherQueue,
                          new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(
                          reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID),
                          RetransmissionTime, reloadDialog.Execute));
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                          "PreJoinProcedure: " + ex.Message);
                    }

                    yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

                    if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                    {
                        if (reloadDialog.ReceivedMessage.TransactionID == bsTransId)
                        {

                            if (reloadDialog.ReceivedMessage.forwarding_header.via_list.Count == 1)
                            {
                                BootstrapServer bsServer = BootstrapServerList[iCycleBootstrap - 1];
                                bsServer.NodeId = reloadDialog.ReceivedMessage.forwarding_header.via_list[0].destination_data.node_id;
                                BootstrapServerList.RemoveAt(iCycleBootstrap - 1);
                                BootstrapServerList.Insert(iCycleBootstrap - 1, bsServer);
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("Bootstrap ID: {0}", reloadDialog.ReceivedMessage.forwarding_header.via_list[0].destination_data.node_id));
                                //Console.WriteLine("Bootstrap ID: {0}", reloadDialog.ReceivedMessage.forwarding_header.via_list[0].destination_data.node_id);
                            }
                            else if (reloadDialog.ReceivedMessage.forwarding_header.via_list.Count == 2)
                            {
                                BootstrapServer bsServer = BootstrapServerList[iCycleBootstrap - 1];
                                bsServer.NodeId = reloadDialog.ReceivedMessage.forwarding_header.via_list[1].destination_data.node_id;
                                BootstrapServerList.RemoveAt(iCycleBootstrap - 1);
                                BootstrapServerList.Insert(iCycleBootstrap - 1, bsServer);
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("Bootstrap ID: {0}", reloadDialog.ReceivedMessage.forwarding_header.via_list[1].destination_data.node_id));
                                //Console.WriteLine("Bootstrap ID: {0}", reloadDialog.ReceivedMessage.forwarding_header.via_list[1].destination_data.node_id);
                            }

                            bsTransId = 0;
                        }

                        break;
                    }




                    /* still bootstrapping, allow cycling trough different bootstraps by
                     * resetting NextHopNode
                     */
                    if (i == 0)
                        NextHopNode = null;

                    /* If a response has not been received when the timer fires, the request
                       is retransmitted with the same transaction identifier. 
                    */
                    --iRetrans;
                    if (iRetrans >= 0)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} Attach  {1} TransId={2:x16}", iRetrans, NextHopNode, reloadSendMsg.TransactionID));
                        m_statistics.IncRetransmission();
                    }
                    else
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! Attach {0} TransId={1:x16}", NextHopNode, reloadSendMsg.TransactionID));
                        m_statistics.IncTransmissionError();
                        if (ReloadGlobals.AutoExe)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoin: Exit because initial Attach Faild!");
                            m_machine.SendCommand("Exit");
                        }
                    }
                }
                try
                {
                    if (reloadDialog != null && !reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                    {
                        //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                        reloadRcvMsg = reloadDialog.ReceivedMessage;
                        RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                        if (reloadRcvMsg != null)
                        {
                            if (msgCode == RELOAD_MessageCode.Attach_Answer)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                                  String.Format("{0} <== {1} last={2} TransId={3:x16}",
                                  msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                                  reloadRcvMsg.LastHopNodeId, reloadRcvMsg.TransactionID));

                                AttachReqAns answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

                                if (answ != null)
                                {
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

                                    if (m_ReloadConfig.IamClient)
                                    {
                                        m_ReloadConfig.LastJoinedTime = DateTime2.Now;
                                        TimeSpan joiningTime = m_ReloadConfig.LastJoinedTime - m_ReloadConfig.StartJoinMobile;
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                                          "Join:" + joiningTime.TotalSeconds.ToString());
                                    }

                                    attached = true;
                                }
                            }
                            else if (msgCode == RELOAD_MessageCode.Error)
                            {
                                if (dest.type == DestinationType.node)
                                {
                                    ErrorResponse error = (ErrorResponse)reloadRcvMsg.reload_message_body;

                                    if (error != null)
                                    {
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
                        else
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: reloadRcvMsg == null!!");
                        }

                        last_destination = dest;
                        dest = new Destination(new ResourceId(reloadRcvMsg.OriginatorID) + (byte)1);
                    }
                    else
                        break;
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: " + ex.Message);
                }
            } // End Successor Search

            // FingerTable enrichment
            if (!m_ReloadConfig.IamClient)
            {
                List<FTEntry> fingers = m_topology.routing_table.AttachFingers();
                Port<bool> attachNextPort = null;
                Boolean attachNext = true;
                /* JP SHOULD send Attach requests to initiate connections to each of
                 * the peers in the neighbor table as well as to the desired finger
                 * table entries.
                 */
                foreach (FTEntry finger in fingers)
                {
                    attachNextPort = new Port<bool>();
                    Arbiter.Activate(m_DispatcherQueue,
                      new IterativeTask<FTEntry, Port<bool>>(
                      finger, attachNextPort, AttachFinger));
                    /* Wait for finger attach */
                    yield return Arbiter.Receive(false, attachNextPort, next =>
                    {
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
                if (!m_ReloadConfig.IamClient)
                {
                    m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joining;
                    m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joining);

                    m_topology.routing_table.SetWaitForJoinAnsw(
                      m_ReloadConfig.AdmittingPeer.Id, true);

                    reloadSendMsg = create_join_req(
                      new Destination(m_ReloadConfig.AdmittingPeer.Id));
                    ReloadDialog reloadDialog = null;

                    int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
                    int iRetrans = ReloadGlobals.MaxRetransmissions;

                    while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
                    {
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
                        if (iRetrans >= 0)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Retrans {0} Join  {1} TransId={2:x16}", iRetrans, m_ReloadConfig.AdmittingPeer, reloadSendMsg.TransactionID));
                            m_statistics.IncRetransmission();
                        }
                        else
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Failed! Join {0} TransId={1:x16}", m_ReloadConfig.AdmittingPeer, reloadSendMsg.TransactionID));
                            m_statistics.IncTransmissionError();
                        }
                    }

                    try
                    {
                        if (!reloadDialog.Error)
                        {
                            reloadRcvMsg = reloadDialog.ReceivedMessage;
                            RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                            if (reloadRcvMsg != null)
                            {
                                if (msgCode == RELOAD_MessageCode.Join_Answer)
                                {
                                    m_topology.routing_table.SetWaitForJoinAnsw(reloadRcvMsg.OriginatorID, false);

                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                                      String.Format("{0} <== {1} TransId={2:x16}",
                                      msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                                      reloadRcvMsg.TransactionID));

                                    NodeState nodestate = m_topology.routing_table.GetNodeState(reloadRcvMsg.OriginatorID);

                                    if (nodestate == NodeState.updates_received)
                                    {
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
                                    else
                                    {
                                        m_ReloadConfig.LastJoinedTime = DateTime.Now;
                                        TimeSpan joiningTime = m_ReloadConfig.LastJoinedTime - m_ReloadConfig.StartTime;
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, String.Format("Join:{0}", joiningTime.TotalSeconds.ToString()));

                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                                          String.Format("Prejoin: nodestate != update_recv at Node {0}", m_machine.ReloadConfig.ListenPort));
                                    }
                                    //m_topology.routing_table.SendUpdatesToAllFingers();
                                }
                            }
                            else
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: reloadRcvMsg == null!!");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "PreJoinProcedure: " + ex.Message);
                    }
                }
                else
                {
                    if (m_ReloadConfig.SipUri == "")
                        m_ReloadConfig.SipUri = String.Format("{0}@{1}", ReloadGlobals.HostName,
                            m_ReloadConfig.OverlayName);

                    if (m_ReloadConfig.SipUri != null && m_ReloadConfig.SipUri != "")
                    {
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
            else
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                  String.Format("PreJoinPredure => Node {0} has no admitting peer = {1}!",
                  m_machine.ReloadConfig.ListenPort, m_ReloadConfig.AdmittingPeer));
                if (ReloadGlobals.AutoExe)
                {
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
          Port<bool> attachNext)
        {
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
            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, nextHopNode);
                    m_forwarding.LoopedTrans = new Port<UInt64>();
                    if (reloadDialog == null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                        "ReloadDialog null!");

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                      String.Format("Finger-{0} via {1} ==> Dest={2} TransId={3:x16}",
                      RELOAD_MessageCode.Attach_Request.ToString().PadRight(16, ' '),
                      nextHopNode, dest.ToString(), reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue,
                      new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
                      new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime,
                      reloadDialog.Execute));
                }
                catch (Exception e)
                {
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
                  Arbiter.Receive(false, m_forwarding.LoopedTrans, transId =>
                  {
                      if (transId == reloadSendMsg.TransactionID)
                      {
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                            String.Format("Not re-sending transaction: {0:x16}=> a loopback detected!",
                            reloadSendMsg.TransactionID));
                          gotLoop = true;
                          m_forwarding.LoopedTrans = new Port<ulong>();
                      }
                  }));


                if (gotLoop)
                {
                    attachNext.Post(true);
                    yield break; ;
                }

                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                    break;

                // If a response has not been received when the timer fires, the request
                // is retransmitted with the same transaction identifier. 

                --iRetrans;
                if (iRetrans >= 0)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                      String.Format("Retrans {0} SendAttach  via {1} TransId={2:x16}",
                      iRetrans, nextHopNode, reloadSendMsg.TransactionID));
                    m_ReloadConfig.Statistics.IncRetransmission();
                }
                else
                {
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

            try
            {
                if (reloadDialog != null && !reloadDialog.Error &&
                  reloadDialog.ReceivedMessage != null)
                {
                    /*the SourceNodeID delivered from reloadDialog comes from connection
                     * table and is the last hop of the message
                     */
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                    RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                    AttachReqAns answ = null;
                    if (msgCode == RELOAD_MessageCode.Attach_Answer)
                    {
                        answ = (AttachReqAns)reloadRcvMsg.reload_message_body;

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                          String.Format("Finger-{0} <== {1} TransId={2:x16}",
                          msgCode.ToString().PadRight(16, ' '),
                          reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

                        Node originator = new Node(reloadRcvMsg.OriginatorID, answ.ice_candidates);
                        NodeState nodeState = m_topology.routing_table.GetNodeState(originator.Id);
                        if (nodeState == NodeState.unknown)
                        {
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
            catch (Exception e)
            {
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
        public IEnumerator<ITask> Store(string ResourceName, List<StoreKindData> kind_data)
        {
            if (m_ReloadConfig.IamClient)
                m_ReloadConfig.StartStoreMobile = DateTime2.Now;
            else
                m_ReloadConfig.StartStore = DateTime.Now;

            ReloadDialog reloadDialog = null;
            ReloadMessage reloadSendMsg;
            ResourceId res_id = new ResourceId(ResourceName);

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1}", ResourceName, res_id));
            Node node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

            if (m_ReloadConfig.IamClient && node == null)
            {
                node = m_ReloadConfig.AdmittingPeer;
            }
            if (node == null || node.Id == m_ReloadConfig.LocalNodeID)
            {
                foreach (StoreKindData storeKindData in kind_data)
                {

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                      String.Format("Local storage at NodeId {0}", node.Id));
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                      "Store:0,011111");
                    m_topology.Store(res_id, storeKindData);
                }

                // REPLICATEST
                // incoming store request is not a replication request
                int numberReplicas = m_topology.routing_table.Successors.Count >= 2 ? 2 : m_topology.routing_table.Successors.Count; // Replica number is max 2
                // send replica to all successors
                for (int i = 0; i < numberReplicas; i++)
                {
                    NodeId successorNode = m_topology.routing_table.Successors[i];
                    ReloadMessage replica = create_store_req(new Destination(successorNode), res_id, kind_data, true);
                    send(replica, m_topology.routing_table.GetNode(successorNode));
                }

                if (storeDone != null) storeDone.Post(reloadDialog);
                yield break;
            }

            Destination dest = new Destination(res_id);

            reloadSendMsg = create_store_req(dest, kind_data, false);

            int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

            int iRetrans = ReloadGlobals.MaxRetransmissions;

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
            {
                try
                {
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                        reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue,
                        new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
                            new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                }
                catch (Exception ex)
                {
                    storeDone.Post(reloadDialog);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
                    ReloadGlobals.PrintException(m_ReloadConfig, ex);
                    break;
                }

                yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

                //if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null && reloadDialog.ReceivedMessage.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                //    break;

                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null && reloadDialog.ReceivedMessage.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                {

                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (dest.type == DestinationType.node)
                    {
                        if (reloadRcvMsg.OriginatorID != dest.destination_data.node_id)
                        {
                            // drop message and retransmit request
                            reloadRcvMsg = null;
                        }
                    }
                    else if (dest.type == DestinationType.resource)
                    {
                        int suc = m_topology.routing_table.GetSuccessorCount(false);
                        int pre = m_topology.routing_table.GetPredecessorCount(false);

                        if (suc >= 2 && pre >= 2)
                        {
                            // check if resource is mapping to a node in my routing table
                            if (dest.destination_data.ressource_id.ElementOfInterval(
                                m_topology.routing_table.Predecessors[pre - 2],
                                m_topology.routing_table.Successors[suc - 1],
                                true)
                            )
                            {
                                if (reloadRcvMsg.OriginatorID < m_topology.routing_table.Predecessors[pre - 2] && reloadRcvMsg.OriginatorID > m_topology.routing_table.Successors[suc - 1])
                                {
                                    // drop message and retransmit request
                                    reloadRcvMsg = null;
                                }
                            }
                        }
                    }
                    if (reloadRcvMsg != null)
                        break;
                }


                /* If a response has not been received when the timer fires, the request
                   is retransmitted with the same transaction identifier. 
                */
                --iRetrans;
            }

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                    {
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
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                      String.Format("Store failed"));
                    m_statistics.IncTransmissionError();
                }
            }
            catch (Exception ex)
            {
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
        public IEnumerator<ITask> Store(string ResourceName, List<StoreKindData> kind_data, NodeId viaGateWay)
        {
            if (m_ReloadConfig.IamClient)
                m_ReloadConfig.StartStoreMobile = DateTime2.Now;
            else
                m_ReloadConfig.StartStore = DateTime.Now;

            ReloadDialog reloadDialog = null;
            ReloadMessage reloadSendMsg;
            ResourceId res_id = new ResourceId(ResourceName);
            //List<StoreKindData> kind_data = new List<StoreKindData>();


            Node node = null;

            if (viaGateWay != null)
            {
                //NodeId gateway = new ResourceId(viaGateWay);

                node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1} via Gateway {2}", ResourceName, res_id, viaGateWay));

                if (m_ReloadConfig.IamClient && node == null)
                {
                    node = m_ReloadConfig.AdmittingPeer;
                }

                foreach (StoreKindData storeKindData in kind_data)
                {
                    if (node == null || node.Id == m_ReloadConfig.LocalNodeID)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Local storage at NodeId {0}", node.Id));
                        m_topology.Store(res_id, storeKindData);
                        yield break;
                    }
                }

                Destination gateway = new Destination(new NodeId(viaGateWay.Data));
                Destination storeDestination = new Destination(res_id);
                StoreReq storeRequest = new StoreReq(storeDestination.destination_data.ressource_id,
                                                      kind_data,
                                                      m_machine.UsageManager, false);
                reloadSendMsg = create_reload_message(gateway, ++m_ReloadConfig.TransactionID, storeRequest);
                reloadSendMsg.forwarding_header.destination_list.Add(storeDestination);  //this is the real destination

                if (reloadSendMsg.AddDestinationOverlay(ResourceName))
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AddDestinationOverlay successful");
            }
            else
            {
                res_id = new ResourceId(ResourceName);

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Store {0} as ResID: {1}", ResourceName, res_id));
                node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

                if (m_ReloadConfig.IamClient && node == null)
                {
                    node = m_ReloadConfig.AdmittingPeer;
                }
                if (node == null || node.Id == m_ReloadConfig.LocalNodeID)
                {
                    foreach (StoreKindData storeKindData in kind_data)
                    {

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                          String.Format("Local storage at NodeId {0}", node.Id));
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE,
                          "Store:0,011111");
                        m_topology.Store(res_id, storeKindData);
                    }
                    if (storeDone != null) storeDone.Post(reloadDialog);
                    yield break;
                }
                reloadSendMsg = create_store_req(new Destination(res_id), kind_data, false);
            }

            int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

            int iRetrans = ReloadGlobals.MaxRetransmissions;

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
            {
                try
                {
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                        reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue,
                        new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg,
                            new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                }
                catch (Exception ex)
                {
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

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                    {
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
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                      String.Format("Store failed"));
                    m_statistics.IncTransmissionError();
                }
            }
            catch (Exception ex)
            {
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
        public IEnumerator<ITask> Fetch(string resourceName, List<StoredDataSpecifier> specifiers, NodeId viaGateWay)
        {
            ReloadDialog reloadDialog = null;
            ReloadMessage reloadSendMsg;
            List<IUsage> recUsages = new List<IUsage>();
            ResourceId res_id = new ResourceId(resourceName);

            Node node = null;
            List<FetchKindResponse> fetchKindResponses = new List<FetchKindResponse>();
            FetchKindResponse fetchKindResponse = null;

            if (viaGateWay != null)
            {

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Fetch {0} as ResID: {1} via Gateway {2}", resourceName, res_id, viaGateWay));

                node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);
                //node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

                if (m_ReloadConfig.IamClient && node == null)
                {
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
            else
            {

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
                  String.Format("Fetch {0} as ResID: {1}", resourceName, res_id));

                node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

                if (m_ReloadConfig.IamClient && node == null)
                {
                    node = m_ReloadConfig.AdmittingPeer;
                }

                List<Destination> dest_list = new List<Destination>();
                dest_list.Add(new Destination(m_topology.LocalNode.Id));

                if (node == null || node.Id == m_ReloadConfig.LocalNodeID)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Local Fetch.");
                    m_ReloadConfig.StartFetchAttach = DateTime.Now;
                    foreach (StoredDataSpecifier specifier in specifiers)
                    {
                        var responses = new List<FetchKindResponse>();
                        if (m_topology.Fetch(res_id, specifier, out fetchKindResponse))
                        {
                            responses.Add(fetchKindResponse);
                            foreach (StoredData sd in fetchKindResponse.values)
                            {
                                if (m_ReloadConfig.AccessController.validateDataSignature(res_id, fetchKindResponse.kind, sd))
                                    recUsages.Add(sd.Value.GetUsageValue);
                                else
                                {
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


                    if (fetchDone != null)
                    {
                        if (recUsages.Count == 0)
                        {
                            foreach (StoredDataSpecifier specifier in specifiers)
                                recUsages.Add(new NoResultUsage(specifier.ResourceName));
                        }
                        fetchDone.Post(recUsages);
                    }
                    yield break;
                }
                else
                {
                    reloadSendMsg = create_fetch_req(new Destination(res_id), specifiers);
                }
            }

            int RetransmissionTime = ReloadGlobals.RetransmissionTime +
              ReloadGlobals.MaxTimeToSendPacket;

            int iRetrans = ReloadGlobals.MaxRetransmissions;

            while (iRetrans >= 0 &&
              m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
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
                catch (Exception ex)
                {
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

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                    RELOAD_MessageCode recMsgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                    if (recMsgCode == RELOAD_MessageCode.Fetch_Answer)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                          String.Format("{0} <== {1} TransId={2:x16}",
                            recMsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                              reloadRcvMsg.TransactionID));
                        FetchAns answ = (FetchAns)reloadRcvMsg.reload_message_body;

                        if (answ != null)
                        {
                            fetchKindResponses = answ.KindResponses;
                            foreach (FetchKindResponse kind in fetchKindResponses)
                            {
                                foreach (StoredData sd in kind.values)
                                {
                                    if (m_ReloadConfig.AccessController.validateDataSignature(res_id, kind.kind, sd))
                                        recUsages.Add(sd.Value.GetUsageValue);
                                    else
                                    {
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!!");
                                    }
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                                      String.Format("Fetch successful, got {0}",
                                        sd.Value.GetUsageValue.Report()));
                                }
                            }
                            OnFetchedData(res_id, fetchKindResponses);
                            if (fetchDone != null)
                            {
                                if (recUsages.Count == 0)
                                {
                                    foreach (StoredDataSpecifier specifier in specifiers)
                                        recUsages.Add(new NoResultUsage(specifier.ResourceName));
                                }
                                fetchDone.Post(recUsages);
                            }
                        }
                    }
                }
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Fetch failed"));
                    foreach (StoredDataSpecifier specifier in specifiers)
                        recUsages.Add(new NoResultUsage(specifier.ResourceName));
                    fetchDone.Post(recUsages);
                    m_statistics.IncTransmissionError();
                }
            }
            catch (Exception ex)
            {
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
        public IEnumerator<ITask> AppAttachProcedure(Destination dest, NodeId viaGateWay, string overlayName)
        {
            ReloadMessage reloadSendMsg;
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure to {0} via GateWay {1}", dest, viaGateWay));
            reloadSendMsg = create_app_attach_req(new Destination(new NodeId(viaGateWay.Data)));
            reloadSendMsg.forwarding_header.destination_list.Add(dest);

            if (reloadSendMsg.AddDestinationOverlay(overlayName))
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AddDestinationOverlay successful");

            //create_reload_message(new ReloadMessage(m_ReloadConfig, m_topology.LocalNode.Id, dest, ++m_ReloadConfig.TransactionID, new AppAttachReqAns(m_topology.LocalNode, true)));

            if (dest.type != DestinationType.node)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure failed: unexpected destination type"));
                yield break;
            }

            if (dest.destination_data.node_id == m_topology.Id)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("local AppAttachProcedure dropped"));
                yield break;
            }

            //Node node = m_topology.routing_table.FindNextHopTo(dest.destination_data.node_id, true, false);
            Node node = m_topology.routing_table.FindNextHopTo(viaGateWay, true, false);

            if (node == null)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("AppAttachProcedure: failed, did not found next hop to {0}", dest.destination_data.node_id));
                yield break;
            }

            ReloadDialog reloadDialog = null;

            int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;
            int iRetrans = ReloadGlobals.MaxRetransmissions;

            m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.attaching);

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
                    /* use a new ReloadDialog instance for every usage, Monitor requires it                         */
                    reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} via {1} ==> Dest={2} TransId={3:x16}",
                                                                                            RELOAD_MessageCode.App_Attach_Request.ToString().PadRight(16, ' '),
                                                                                            node,
                                                                                            dest.ToString(),
                                                                                            reloadSendMsg.TransactionID));

                    Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                }
                catch (Exception ex)
                {
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

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                    if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.App_Attach_Answer)
                    {
                        AppAttachReqAns answ = (AppAttachReqAns)reloadRcvMsg.reload_message_body;

                        if (answ != null)
                        {
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
                else
                {
                    m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
                }
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppAttachProcedure: " + ex.Message);
                m_topology.routing_table.SetNodeState(dest.destination_data.node_id, NodeState.unknown);
            }
        }

        #endregion

        /// <summary>
        /// Just the leaving Procedure
        /// </summary>
        /// <returns></returns>
        public IEnumerator<ITask> Leave()
        {
            m_ReloadConfig.State = ReloadConfig.RELOAD_State.Leave;

            foreach (ReloadConnectionTableInfoElement rce in m_flm.ConnectionTable)
            {

                if (m_topology.routing_table.RtTable.ContainsKey(rce.NodeID.ToString()))
                {

                    ReloadDialog reloadDialog = null;
                    ReloadMessage reloadSendMsg = create_leave_req(new Destination(rce.NodeID));

                    int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

                    int iRetrans = ReloadGlobals.MaxRetransmissions;

                    while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
                    {
                        try
                        {
                            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm,
                              m_topology.routing_table.GetNode(rce.NodeID));

                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                              String.Format("{0} ==> {1} TransId={2:x16}",
                              reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
                              rce.NodeID, reloadSendMsg.TransactionID));

                            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                        }
                        catch (Exception ex)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Leave: " + ex.Message);
                        }

                        if (reloadDialog != null)
                        {
                            yield return Arbiter.Receive(false, reloadDialog.Done, done => { });

                            if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                                break;
                        }

                        /* If a response has not been received when the timer fires, the request
                           is retransmitted with the same transaction identifier. 
                        */
                        --iRetrans;
                    }

                    try
                    {
                        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                        {
                            /*the SourceNodeID delivered from reloadDialog comes from
                             * connection table and is the last hop of the message
                             */
                            ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                            RELOAD_MessageCode code = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                            if (code == RELOAD_MessageCode.Leave_Answer)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                                  String.Format("{0} <== {1} TransId={2:x16}",
                                  code.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                                  reloadRcvMsg.TransactionID));
                            }
                        }
                        else
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                              String.Format("Leave failed"));
                            m_statistics.IncTransmissionError();
                        }
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                          "Send Leave: " + ex.Message);
                    }
                }
            }

            //m_machine.SendCommand("Exit");

            // Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, Node>(reloadSendMsg, m_topology.routing_table.GetNode(rce.NodeID), Send));
            // m_ReloadConfig.State = ReloadConfig.RELOAD_State.PreJoin;
            // m_machine.StateUpdates(ReloadConfig.RELOAD_State.PreJoin);
        }


        // REPLICATEST

        public void EvaluateReplicas()
        {
            List<String> ReplicasToRemove = new List<string>();

            foreach (String rep in m_topology.Replicas)
            {
                ResourceId id = new ResourceId(rep);

                if (m_topology.routing_table.Predecessors.Count > 0)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("EvaluateReplicas: Is data {0} in interval {1} - {2}", rep, m_topology.routing_table.Predecessors[0], m_topology.LocalNode.Id));
                else
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Nobody is left. All the data belong to me"));

                if (m_topology.routing_table.Predecessors.Count > 0 && id.ElementOfInterval(m_topology.routing_table.Predecessors[0], m_topology.LocalNode.Id, false))
                {
                    ReplicasToRemove.Add(rep);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("EvaluateReplicas: I'm now responsible for data {0}", rep));

                    if (m_topology.routing_table.Successors.Count > 0)
                        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_topology.routing_table.Successors[0], StoreReplicas));
                    if (m_topology.routing_table.Successors.Count > 1)
                        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_topology.routing_table.Successors[1], StoreReplicas));
                }
            }

            foreach (String rep in ReplicasToRemove)
            {
                m_topology.Replicas.Remove(rep);
            }
        }


        /// <summary>
        /// Store Replicas: My next two successors have changed. Replicate my data
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public IEnumerator<ITask> StoreReplicas(NodeId node)
        {
            // For each Resource stored at this Peer, handover StoredData
            List<string> storedKeys;
            if ((storedKeys = m_topology.Storage.StoredKeys) != null && storedKeys.Count > 0)
            {

                m_topology.Storage.RemoveExpired();

                Dictionary<ResourceId, List<StoreKindData>> nodes = new Dictionary<ResourceId, List<StoreKindData>>();

                foreach (string key in storedKeys)
                {
                    ResourceId res_id = new ResourceId(ReloadGlobals.HexToBytes(key));

                    if (!m_topology.Replicas.Contains(key))
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Store Replicas - will send store requests");
                        if (!nodes.ContainsKey(res_id))
                        {
                            nodes.Add(res_id, new List<StoreKindData>());
                            nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));
                        }
                        else
                        {
                            nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));
                        }
                    }
                }

                ReloadDialog reloadDialog = null;
                ReloadMessage reloadSendMsg;
                List<StoreKindData> storeKindData;

                foreach (ResourceId res_id in nodes.Keys)
                {
                    storeKindData = nodes[res_id];

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "GOING TO REPLICATE UNDER RES_ID: " + res_id + " AT NODE: " + node);

                    List<SignerIdentity> signers = new List<SignerIdentity>();

                    foreach (StoreKindData skd in storeKindData)
                    {
                        foreach (StoredData sd in skd.Values)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "STOREDATA: " + sd.Value.GetUsageValue.Report());

                            // add certificate
                            if (!signers.Contains(sd.Signature.Identity))
                                signers.Add(sd.Signature.Identity);
                        }
                    }

                    reloadSendMsg = create_store_req(new Destination(node), res_id, storeKindData, true);

                    // get certificates for this data
                    List<GenericCertificate> certs = new List<GenericCertificate>();
                    certs.AddRange(m_ReloadConfig.AccessController.GetPKCs(signers));

                    // add certificates to fetch answer
                    reloadSendMsg.security_block.Certificates.AddRange(certs);

                    int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

                    int iRetrans = ReloadGlobals.MaxRetransmissions;

                    while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
                    {
                        try
                        {
                            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, m_topology.routing_table.FindNextHopTo(node, false, false));

                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                                reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node, reloadSendMsg.TransactionID));

                            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                        }
                        catch (Exception ex)
                        {
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

                    try
                    {
                        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                        {
                            ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                            if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

                                //StoreReqAns answ = (StoreReqAns)reloadRcvMsg.reload_message_body; --old
                                StoreAns answ = (StoreAns)reloadRcvMsg.reload_message_body; // --alex

                                if (answ != null)
                                {
                                }
                            }
                        }
                        else
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Store Replica failed"));
                            m_statistics.IncTransmissionError();
                        }
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store Replica: " + ex.Message);
                    }
                }
            }
        }



        /// <summary>
        /// Handover key if: 1. leave overlay 2. I'm AP while a join req happens.
        /// </summary>
        /// <param name="fSendLeaveFirst"></param>
        /// <returns></returns>
        public IEnumerator<ITask> HandoverKeys(bool fSendLeaveFirst)
        {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Handover Keys!");

            if (fSendLeaveFirst)
            {
                yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask(Leave));
            }

            // For each Resource stored at this Peer, handover StoredData
            List<string> storedKeys;
            if ((storedKeys = m_topology.Storage.StoredKeys) != null && storedKeys.Count > 0)
            {

                m_topology.Storage.RemoveExpired();

                Dictionary<ResourceId, List<StoreKindData>> nodes = new Dictionary<ResourceId, List<StoreKindData>>();

                Dictionary<ResourceId, Node> destinations = new Dictionary<ResourceId, Node>();

                foreach (string key in storedKeys)
                {
                    ResourceId res_id = new ResourceId(ReloadGlobals.HexToBytes(key));
                    Node currentNode = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, fSendLeaveFirst);
                    if (currentNode == null || currentNode.Id == m_ReloadConfig.LocalNodeID)
                    {
                        //everything's fine, key still belongs to me
                        continue;
                    }
                    // REPLICATEST
                    // peer is no longer in the replica set for the resource
                    else if (m_topology.routing_table.Predecessors.Count > 2 && !res_id.ElementOfInterval(m_topology.routing_table.Predecessors[2], m_topology.LocalNode.Id, false))
                    {
                        m_topology.Storage.Remove(res_id.ToString());
                        m_topology.Replicas.Remove(res_id.ToString());
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Data {0} no longer in range. Delete replica", res_id.ToString()));
                    }
                    else
                    {
                        if (!m_topology.Replicas.Contains(key))
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Handover Keys - will send store requests");
                            if (!nodes.ContainsKey(res_id))
                            {
                                nodes.Add(res_id, new List<StoreKindData>());
                                nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));

                                destinations.Add(res_id, currentNode);
                            }
                            else
                            {
                                nodes[res_id].AddRange(m_topology.Storage.GetStoreKindData(key));
                            }
                        }
                    }
                }

                ReloadDialog reloadDialog = null;
                ReloadMessage reloadSendMsg;
                List<StoreKindData> storeKindData;

                foreach (ResourceId res_id in nodes.Keys)
                {
                    Node node = destinations[res_id];
                    storeKindData = nodes[res_id];

                    List<SignerIdentity> signers = new List<SignerIdentity>();

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "GOING TO STORE UNDER RES_ID: " + res_id);

                    foreach (StoreKindData skd in storeKindData)
                    {
                        foreach (StoredData sd in skd.Values)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "STOREDATA: " + sd.Value.GetUsageValue.Report());

                            // add certificate
                            if (!signers.Contains(sd.Signature.Identity))
                                signers.Add(sd.Signature.Identity);
                        }
                    }

                    if (m_machine.ReloadConfig.State == ReloadConfig.RELOAD_State.Leave)
                    {
                        node = m_topology.routing_table.GetSuccessor(0);
                        reloadSendMsg = create_store_req(new Destination(node.Id), res_id, storeKindData, false);
                    }
                    else
                    {
                        reloadSendMsg = create_store_req(new Destination(res_id), storeKindData, false);
                    }

                    // get certificates for this data
                    List<GenericCertificate> certs = new List<GenericCertificate>();
                    certs.AddRange(m_ReloadConfig.AccessController.GetPKCs(signers));

                    // add certificates to fetch answer
                    reloadSendMsg.security_block.Certificates.AddRange(certs);

                    int RetransmissionTime = ReloadGlobals.RetransmissionTime + ReloadGlobals.MaxTimeToSendPacket;

                    int iRetrans = ReloadGlobals.MaxRetransmissions;

                    while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
                    {
                        try
                        {
                            reloadDialog = new ReloadDialog(m_ReloadConfig, m_flm, node);

                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                                reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), node.Id, reloadSendMsg.TransactionID));

                            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessage, ReloadMessageFilter, int>(reloadSendMsg, new ReloadMessageFilter(reloadSendMsg.TransactionID), RetransmissionTime, reloadDialog.Execute));
                        }
                        catch (Exception ex)
                        {
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

                    try
                    {
                        if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                        {
                            ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;

                            if (reloadRcvMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadRcvMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID, reloadRcvMsg.TransactionID));

                                //StoreReqAns answ = (StoreReqAns)reloadRcvMsg.reload_message_body; --old
                                StoreAns answ = (StoreAns)reloadRcvMsg.reload_message_body; // --alex

                                if (answ != null)
                                {
                                    // m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Delete Key {0}", res_id));
                                    // m_topology.StoredValues.Remove(StoredKey); --old

                                    // REPLICATEST
                                    // Keep stored data but mark it as replica
                                    if (!m_topology.Replicas.Contains(res_id.ToString()))
                                        m_topology.Replicas.Add(res_id.ToString());

                                    //m_topology.Storage.Remove(res_id.ToString());
                                }
                            }
                        }
                        else
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Store failed"));
                            m_statistics.IncTransmissionError();
                        }
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send Store: " + ex.Message);
                    }
                }
            }

            // this code might be redundant to code in RoutingTable.Leave()

            //// check if there are replicas I should be responsible for
            //if (m_topology.routing_table.Predecessors.Count == 0)
            //{
            //    m_topology.Replicas.Clear();
            //}
            //else
            //{
            //    List<string> removeReplicas = new List<string>();
            //    foreach (string replica in m_topology.Replicas)
            //    {
            //        // Convert the Resource String in a ResourceId
            //        int NumberChars = replica.Length;
            //        byte[] bytes = new byte[NumberChars / 2];
            //        for (int i = 0; i < NumberChars; i += 2)
            //            bytes[i / 2] = Convert.ToByte(replica.Substring(i, 2), 16);
            //        ResourceId id = new ResourceId(bytes);

            //        if (id.ElementOfInterval(m_topology.routing_table.Predecessors[0], m_topology.LocalNode.Id, false))
            //        {
            //            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("ResId: {0} is in interval [{1} - {2}]", id, m_topology.routing_table.Predecessors[0], m_topology.LocalNode.Id));
            //            removeReplicas.Add(replica);
            //        }
            //    }
            //    foreach (string removeRep in removeReplicas)
            //    {
            //        m_topology.Replicas.Remove(removeRep);
            //        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_topology.routing_table.Successors[0], StoreReplicas));
            //        if(m_topology.routing_table.Successors.Count > 1)
            //            Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_topology.routing_table.Successors[1], StoreReplicas));
            //    }
            //}

            if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Leave)
                m_machine.SendCommand("Exit");

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
        public IEnumerator<ITask> Fetch(string resourceName, List<StoredDataSpecifier> specifiers)
        {
            ReloadDialog reloadDialog = null;
            ReloadMessage reloadSendMsg;
            List<IUsage> recUsages = new List<IUsage>();
            ResourceId res_id = new ResourceId(resourceName);

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                String.Format("Fetch {0} as ResID: {1}", resourceName, res_id));

            Node node = m_topology.routing_table.FindNextHopTo(new NodeId(res_id), true, false);

            if (m_ReloadConfig.IamClient && node == null)
            {
                node = m_ReloadConfig.AdmittingPeer;
            }

            List<Destination> dest_list = new List<Destination>();
            dest_list.Add(new Destination(m_topology.LocalNode.Id));
            List<FetchKindResponse> fetchKindResponses = new List<FetchKindResponse>();
            FetchKindResponse fetchKindResponse = null;

            if (node == null || node.Id == m_ReloadConfig.LocalNodeID)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Local Fetch.");
                m_ReloadConfig.StartFetchAttach = DateTime.Now;
                foreach (StoredDataSpecifier specifier in specifiers)
                {
                    var responses = new List<FetchKindResponse>();
                    if (m_topology.Fetch(res_id, specifier, out fetchKindResponse))
                    {
                        responses.Add(fetchKindResponse);
                        foreach (StoredData sd in fetchKindResponse.values)
                        {
                            if (m_ReloadConfig.AccessController.validateDataSignature(res_id, fetchKindResponse.kind, sd))
                                recUsages.Add(sd.Value.GetUsageValue);
                            else
                            {
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


                if (fetchDone != null)
                {
                    if (recUsages.Count == 0)
                    {
                        foreach (StoredDataSpecifier specifier in specifiers)
                            recUsages.Add(new NoResultUsage(specifier.ResourceName));
                    }
                    fetchDone.Post(recUsages);
                }
                yield break;
            }
            else
            {
                reloadSendMsg = create_fetch_req(new Destination(res_id), specifiers);
            }

            int RetransmissionTime = ReloadGlobals.RetransmissionTime +
              ReloadGlobals.MaxTimeToSendPacket;

            int iRetrans = ReloadGlobals.MaxRetransmissions;

            while (iRetrans >= 0 &&
              m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
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
                catch (Exception ex)
                {
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

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                    RELOAD_MessageCode recMsgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                    if (recMsgCode == RELOAD_MessageCode.Fetch_Answer)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                          String.Format("{0} <== {1} TransId={2:x16}",
                            recMsgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                              reloadRcvMsg.TransactionID));
                        FetchAns answ = (FetchAns)reloadRcvMsg.reload_message_body;

                        // TODO: For now add certificate to global PKC Store, but they are only temporarilly needed in validateDataSignature
                        m_ReloadConfig.AccessController.SetPKCs(reloadRcvMsg.security_block.Certificates);

                        if (answ != null)
                        {
                            fetchKindResponses = answ.KindResponses;
                            foreach (FetchKindResponse kind in fetchKindResponses)
                            {
                                foreach (StoredData sd in kind.values)
                                {
                                    if (m_ReloadConfig.AccessController.validateDataSignature(res_id, kind.kind, sd))
                                        recUsages.Add(sd.Value.GetUsageValue);
                                    else
                                    {
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!!");
                                    }
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                                      String.Format("Fetch successful, got {0}",
                                        sd.Value.GetUsageValue.Report()));
                                }
                            }
                            OnFetchedData(res_id, fetchKindResponses);
                            if (fetchDone != null)
                            {
                                if (recUsages.Count == 0)
                                {
                                    foreach (StoredDataSpecifier specifier in specifiers)
                                        recUsages.Add(new NoResultUsage(specifier.ResourceName));
                                }
                                fetchDone.Post(recUsages);
                            }
                        }
                    }
                }
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Fetch failed"));
                    foreach (StoredDataSpecifier specifier in specifiers)
                        recUsages.Add(new NoResultUsage(specifier.ResourceName));
                    fetchDone.Post(recUsages);
                    m_statistics.IncTransmissionError();
                }
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Fetch: " + ex.Message);
            }
        }


        /// <summary>
        /// On data fetch execute the Usages AppProcedure
        /// </summary>
        /// <param name="res_id"></param>
        /// <param name="fetchKindResponse"></param>
        private void OnFetchedData(ResourceId res_id,
          List<FetchKindResponse> fetchKindResponses)
        {
            foreach (var fetchKindResponse in fetchKindResponses)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
                  String.Format("Fetch on {0} returns {1}",
                    res_id, fetchKindResponse.ToString()));
            }

            m_machine.UsageManager.AppProcedure(fetchKindResponses);

        }

        public IEnumerator<ITask> Fetch()
        {
            throw new NotImplementedException();
        }

        public IEnumerator<ITask> SendUpdate(Node node, Node nexthopnode)
        {
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

            while (iRetrans >= 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
            {
                try
                {
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
                catch (Exception ex)
                {
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
                if (iRetrans > 0)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                      String.Format("Retrans {0} SendUpdate  {1}:{2} TransId={3:x16}",
                      iRetrans, node, nexthopnode, reloadSendMsg.TransactionID));
                    m_statistics.IncRetransmission();
                }
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      String.Format("Failed! SendUpdate  {0}:{1} TransId={2:x16}",
                      node, nexthopnode, reloadSendMsg.TransactionID));
                    m_statistics.IncTransmissionError();
                }
            }

            try
            {
                if (!reloadDialog.Error && reloadDialog.ReceivedMessage != null)
                {
                    //the SourceNodeID delivered from reloadDialog comes from connection table and is the last hop of the message
                    ReloadMessage reloadRcvMsg = reloadDialog.ReceivedMessage;
                    RELOAD_MessageCode msgCode = reloadRcvMsg.reload_message_body.RELOAD_MsgCode;
                    if (msgCode == RELOAD_MessageCode.Update_Answer)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                          String.Format("{0} <== {1} TransId={2:x16}",
                          msgCode.ToString().PadRight(16, ' '), reloadRcvMsg.OriginatorID,
                          reloadRcvMsg.TransactionID));

                        UpdateReqAns answ = (UpdateReqAns)reloadRcvMsg.reload_message_body;

                        if (answ != null)
                        {
                            NodeId originator = reloadRcvMsg.OriginatorID;

                            if (m_topology.routing_table.FingerSuccessors.Contains(originator))
                            {
                                //m_topology.routing_table.GetNode(originator).Successors = answ.Successors;
                                //m_topology.routing_table.GetNode(originator).Predecessors = answ.Predecessors;
                                m_topology.routing_table.SetFingerState(originator,
                                  NodeState.updates_received);
                            }
                            if (m_topology.routing_table.RtTable.ContainsKey(originator.ToString()))
                            {
                                m_topology.routing_table.SetNodeState(originator,
                                  NodeState.updates_received);
                                m_topology.routing_table.GetNode(originator).Successors = answ.Successors;
                                m_topology.routing_table.GetNode(originator).Predecessors = answ.Predecessors;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  "Send Update: " + ex.Message);
            }
        }

        public void InboundClose(NodeId nodeid)
        {
            Boolean important_node = m_topology.routing_table.NodeWeNeed(nodeid);

            if (important_node)
                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(nodeid), PingOption.direct, SendPing));
        }

        public Node NextHopToDestination(Destination dest, ref bool direct)
        {
            Node NextHopNode = null;
            direct = false;

            if (dest.type == DestinationType.node)
            {
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
            else
            {
                NextHopNode = m_topology.routing_table.FindNextHopTo(new NodeId(
                  dest.destination_data.ressource_id), true, false);
            }

            if (NextHopNode == null)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  "Did not found next hop to: " + dest);
            return NextHopNode;
        }

        public IEnumerator<ITask> Send(ReloadMessage reloadSendMsg, Node NextHopNode)
        {
            if (reloadSendMsg.reload_message_body.RELOAD_MsgCode ==
              RELOAD_MessageCode.Error)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  String.Format("{0} ==> {1} code={2}: msg=\"{3}\", dest={4}",
                  reloadSendMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
                  NextHopNode, ((ErrorResponse)reloadSendMsg.reload_message_body).ErrorCode,
                  ((ErrorResponse)reloadSendMsg.reload_message_body).ErrorMsg,
                  reloadSendMsg.forwarding_header.destination_list[0]));
            try
            {
                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, ReloadMessage>(NextHopNode, reloadSendMsg, GetForwardingAndLinkManagementLayer().Send));
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  "SendAnswer: " + ex.Message);
            }
            yield break;
        }

        public Boolean CheckAndSetAdmittingPeer(Node node)
        {
            if (!m_ReloadConfig.IsBootstrap)
                if ((m_ReloadConfig.AdmittingPeer == null ||
                     node.Id.ElementOfInterval(m_topology.Id,
                     m_ReloadConfig.AdmittingPeer.Id, true)) &&
                     !(m_ReloadConfig.AdmittingPeer != null &&
                     m_ReloadConfig.AdmittingPeer.Id == node.Id))
                {
                    m_ReloadConfig.AdmittingPeer = node;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                      String.Format("AttachProcedure: Successfully attached to new" +
                        "admitting peer {0}", m_ReloadConfig.AdmittingPeer.Id));
                    return true;
                }

            return false;
        }

        public void reload_attach_inbound(ReloadMessage recmsg)
        {

            try
            {
                AttachReqAns req_answ = (AttachReqAns)recmsg.reload_message_body;
                NodeId OriginatorID = recmsg.OriginatorID;

                if (req_answ != null && req_answ.ice_candidates != null)
                {
                    //if (ReloadGlobals.UseNoIce || m_ReloadConfig.IsBootstrap)
                    //{
                    //Node attacher = new Node(recmsg.OriginatorID, req_answ.ice_candidates);           // markus, moved down
                    //bool isFinger = m_topology.routing_table.isFinger(attacher.Id);

                    //m_topology.routing_table.AddNode(attacher);
                    //m_topology.routing_table.SetNodeState(recmsg.OriginatorID, NodeState.attached);
                    //}


                    // incoming ATTACH REQUEST, so localnode is controlled agent
                    if (recmsg.IsRequest())
                    {

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                                      String.Format("{0} ==> {1} TransId={2:x16}",
                                      RELOAD_MessageCode.Attach_Answer.ToString().PadRight(16, ' '),
                                      OriginatorID, recmsg.TransactionID));

                        ReloadMessage sendmsg = create_attach_answ(
                          new Destination(OriginatorID), recmsg.TransactionID);

                        // log output
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Request: Transaction ID: {0:x}", recmsg.TransactionID));

                        foreach (IceCandidate cand in ((AttachReqAns)sendmsg.reload_message_body).ice_candidates)
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Request: Gathered local candidate for Answer: {0}:{1} (TransId: {2:x})", cand.addr_port.ipaddr.ToString(), cand.addr_port.port, sendmsg.TransactionID));

                        foreach (IceCandidate cand in req_answ.ice_candidates)
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Request: Received remote candidate: {0}:{1} (TransId: {2:x})", cand.addr_port.ipaddr.ToString(), cand.addr_port.port, recmsg.TransactionID));


                        recmsg.PutViaListToDestination(sendmsg);
                        //sendmsg.addOverlayForwardingOptions(recmsg);  //Proprietary  //--Joscha	
                        if (m_machine is GWMachine)
                        { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
                            if (sendmsg.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id)
                            {
                                sendmsg.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
                                ((GWMachine)m_machine).GateWay.interDomainPeer.Transport.receive_message(sendmsg);
                            }
                            else
                                send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
                        }
                        else
                        {
                            send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
                        }




                        // markus
                        if (!ReloadGlobals.UseNoIce) // using ICE
                        {
                            // we only need ICE processing if localnode is a peer (in case of bootstrap we need no checks)
                            if (!m_ReloadConfig.IsBootstrap)
                            {

                                #region ICE TODO
                                // localnode is Peer => ICE processing (this is controlled node)
                                AttachReqAns attachAnswer = (AttachReqAns)sendmsg.reload_message_body;

                                // deep copy of local and remote ice candidates
                                List<IceCandidate> localIceCandidatesCopy = new List<IceCandidate>();
                                List<IceCandidate> remoteIceCandidatesCopy = new List<IceCandidate>();

                                // local candidates
                                foreach (IceCandidate cand in attachAnswer.ice_candidates)
                                {
                                    IceCandidate deepCopy = (IceCandidate)cand.Clone();
                                    localIceCandidatesCopy.Add(deepCopy);
                                }

                                // remote candidates
                                foreach (IceCandidate cand in req_answ.ice_candidates)
                                {
                                    IceCandidate deepCopy = (IceCandidate)cand.Clone();
                                    remoteIceCandidatesCopy.Add(deepCopy);
                                }

                                // now form check list
                                //CheckList checkList = ICE.FormCheckList(attachAnswer.ice_candidates, req_answ.ice_candidates, false);
                                CheckList checkList = ICE.FormCheckList(localIceCandidatesCopy, remoteIceCandidatesCopy, false);

                                ICE.PrintCandidatePairList(checkList.candidatePairs);

                                Console.WriteLine("ThreadId: {0}, send_params einfügen: checkList count {1}", Thread.CurrentThread.ManagedThreadId, checkList.candidatePairs.Count);
                                // Add to connection queue
                                for (int i = 0; i < checkList.candidatePairs.Count; i++)
                                {
                                    ReloadSendParameters send_params = new ReloadSendParameters()
                                    {
                                        connectionTableEntry = null,
                                        destinationAddress = checkList.candidatePairs[i].remoteCandidate.addr_port.ipaddr,
                                        port = checkList.candidatePairs[i].remoteCandidate.addr_port.port,
                                        buffer = null,
                                        frame = false,
                                        done = new Port<bool>(),
                                        // markus
                                        connectionSocket = null,
                                    };


                                    // if key already exists => skip
                                    if (!GetForwardingAndLinkManagementLayer().GetConnectionQueue().ContainsKey(checkList.candidatePairs[i].remoteCandidate))
                                        GetForwardingAndLinkManagementLayer().GetConnectionQueue().Add(checkList.candidatePairs[i].remoteCandidate, send_params);

                                }

                                ICE.ScheduleChecks(checkList, m_ReloadConfig.Logger);

                                // Wait for signals of all succeded candidate pairs. Only one of the succeded candidate pairs is nominated
                                #region signaling
                                // wait for nomination signal
                                List<Thread> waitingThreads = new List<Thread>();

                                foreach (CandidatePair candPair in checkList.candidatePairs)
                                {
                                    if (candPair.state == CandidatePairState.Succeeded)
                                    {
                                        switch (candPair.localCandidate.tcpType)
                                        {
                                            case TcpType.Active:
                                                {
                                                    if (candPair.localCandidate.activeConnectingSocket != null)
                                                    {
                                                        Thread waitThread = new Thread(() =>
                                                        {
                                                            candPair.nominated = ICE.WaitForSignal(candPair.localCandidate.activeConnectingSocket);
                                                        });
                                                        waitingThreads.Add(waitThread);
                                                        waitThread.Start();
                                                    }
                                                }
                                                break;

                                            case TcpType.Passive:
                                                {
                                                    if (candPair.localCandidate.passiveAcceptedSocket != null)
                                                    {
                                                        Thread waitThread = new Thread(() =>
                                                        {
                                                            candPair.nominated = ICE.WaitForSignal(candPair.localCandidate.passiveAcceptedSocket);
                                                        });
                                                        waitingThreads.Add(waitThread);
                                                        waitThread.Start();
                                                    }
                                                }
                                                break;

                                            case TcpType.SO:
                                                {
                                                    if (candPair.localCandidate.soAcceptedSocket != null)
                                                    {
                                                        Thread waitThread = new Thread(() =>
                                                        {
                                                            candPair.nominated = ICE.WaitForSignal(candPair.localCandidate.soAcceptedSocket);
                                                        });
                                                        waitingThreads.Add(waitThread);
                                                        waitThread.Start();
                                                    }

                                                    else if (candPair.localCandidate.soConnectingSocket != null)
                                                    {
                                                        Thread waitThread = new Thread(() =>
                                                        {
                                                            candPair.nominated = ICE.WaitForSignal(candPair.localCandidate.soConnectingSocket);
                                                        });
                                                        waitingThreads.Add(waitThread);
                                                        waitThread.Start();
                                                    }
                                                }
                                                break;

                                        }   // switch
                                    }   // if
                                }   // foreach

                                // wait for all threads
                                foreach (Thread waitingThread in waitingThreads)
                                {
                                    waitingThread.Join();
                                }
                                #endregion


                                // choose pair
                                CandidatePair choosenPair = null;

                                // any nominated pair?
                                if (checkList.candidatePairs.Any(item => item.nominated == true))
                                {
                                    choosenPair = checkList.candidatePairs.First(item => item.nominated == true);
                                }

                                // Close all sockets of all candidate pairs not nominated
                                //for (int i = 0; i < checkList.candidatePairs.Count; i++)
                                //    if ((!checkList.candidatePairs[i].nominated) || (checkList.candidatePairs[i].state != CandidatePairState.Succeeded))
                                //        ICE.CloseAllCandidateSockets(checkList.candidatePairs[i].localCandidate);

                                // add node with chosen remote candidate
                                if (choosenPair != null)
                                {

                                    // save connection
                                    //GetForwardingAndLinkManagementLayer().SaveConnection(choosenPair);

                                    // get connection
                                    Socket socket = GetForwardingAndLinkManagementLayer().GetConnection(choosenPair);

                                    // StartReloadTLSClient
                                    GetForwardingAndLinkManagementLayer().StartReloadTLSClient(OriginatorID, socket);


                                    // for all candidates send_params.done = true
                                    for (int i = 0; i < checkList.candidatePairs.Count; i++)
                                    {
                                        ReloadSendParameters send_params;

                                        GetForwardingAndLinkManagementLayer().GetConnectionQueue().TryGetValue(checkList.candidatePairs[i].remoteCandidate, out send_params);

                                        if (send_params != null)
                                        {
                                            send_params.done.Post(true);

                                            // remove from connection queue
                                            GetForwardingAndLinkManagementLayer().GetConnectionQueue().Remove(checkList.candidatePairs[i].remoteCandidate);
                                        }
                                    }

                                    List<IceCandidate> choosenRemoteCandidates = new List<IceCandidate>();
                                    choosenRemoteCandidates.Add(choosenPair.remoteCandidate);

                                    Node attacher = new Node(recmsg.OriginatorID, choosenRemoteCandidates);
                                    bool isFinger = m_topology.routing_table.isFinger(attacher.Id);

                                    m_topology.routing_table.AddNode(attacher);
                                    m_topology.routing_table.SetNodeState(recmsg.OriginatorID, NodeState.attached);
                                }

                                // free all port mappings created by UPnP
                                foreach (IceCandidate cand in attachAnswer.ice_candidates)
                                {
                                    if (cand.cand_type == CandType.tcp_nat)
                                    {
                                        UPnP upnp = new UPnP();
                                        bool discovered = upnp.Discover(cand.rel_addr_port.ipaddr);

                                        if (discovered)
                                            upnp.DeletePortMapping(cand.addr_port.port, ProtocolType.Tcp);
                                    }
                                }


                                #endregion

                            }


                            else
                            {
                                // localnode is bootstrap => no ICE processing

                                Node attacher = new Node(recmsg.OriginatorID, req_answ.ice_candidates);
                                bool isFinger = m_topology.routing_table.isFinger(attacher.Id);

                                m_topology.routing_table.AddNode(attacher);
                                m_topology.routing_table.SetNodeState(recmsg.OriginatorID, NodeState.attached);

                            }


                        }

                        // using NO ICE
                        else
                        {

                            Node attacher = new Node(recmsg.OriginatorID, req_answ.ice_candidates);
                            bool isFinger = m_topology.routing_table.isFinger(attacher.Id);

                            m_topology.routing_table.AddNode(attacher);
                            m_topology.routing_table.SetNodeState(recmsg.OriginatorID, NodeState.attached);
                        }
                        // markus end



                        if (req_answ.SendUpdate)
                            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, Node>(
                              m_topology.routing_table.GetNode(OriginatorID),
                              m_topology.routing_table.GetNode(recmsg.LastHopNodeId),
                              SendUpdate));



                    }

                    // incoming ATTACH ANSWER, so localnode is controlling agent
                    // and localnode must be a peer, because bootstraps dont create attach requests and because of this cant receive an attach answer
                    else
                    {
                        // using NOICE
                        if (ReloadGlobals.UseNoIce)     // markus: added if/else statement
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                              String.Format("{0} <== {1} (not handled!!)",
                              req_answ.RELOAD_MsgCode.ToString().PadRight(16, ' '), OriginatorID));
                        }

                        // using ICE
                        else
                        {
                            // get local candidates from request
                            List<IceCandidate> localCandidates = null;
                            bool gotLocalCandidate = m_attachRequestCandidates.TryGetValue(recmsg.TransactionID, out localCandidates);

                            // log output
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Answer: Transaction ID: {0:x}", recmsg.TransactionID));
                            foreach (IceCandidate cand in localCandidates)
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Answer: Got local candidate: {0}:{1} (TransId: {2:x})", cand.addr_port.ipaddr.ToString(), cand.addr_port.port, recmsg.TransactionID));

                            foreach (IceCandidate cand in req_answ.ice_candidates)
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Answer: Received remote candidate: {0}:{1} (TransId: {2:x})", cand.addr_port.ipaddr.ToString(), cand.addr_port.port, recmsg.TransactionID));


                            if (req_answ.ice_candidates != null)
                            {
                                // we need ice, except the answering peer is a bootstrap
                                bool needIce = true;

                                //// bootstrap responses with only one candidate
                                //if (req_answ.ice_candidates.Count == 1)
                                //{
                                // is it really a bootstrap?
                                if (req_answ.ice_candidates[0].cand_type == CandType.tcp_bootstrap)
                                {
                                    // attached to a bootstrap, so we have to do nothing here, no ice processing needed
                                    needIce = false;
                                }

                                //}

                                if (needIce)
                                {
                                    #region ICE TODO
                                    // ICE processing (this is controlling node)
                                    if (gotLocalCandidate)
                                    {
                                        // deep copy of remote ice candidates
                                        List<IceCandidate> remoteIceCandidatesCopy = new List<IceCandidate>();

                                        // remote candidates
                                        foreach (IceCandidate cand in req_answ.ice_candidates)
                                        {
                                            IceCandidate deepCopy = (IceCandidate)cand.Clone();
                                            remoteIceCandidatesCopy.Add(deepCopy);
                                        }

                                        //CheckList checkList = ICE.FormCheckList(localCandidates, req_answ.ice_candidates, true);
                                        CheckList checkList = ICE.FormCheckList(localCandidates, remoteIceCandidatesCopy, true);

                                        ICE.PrintCandidatePairList(checkList.candidatePairs);

                                        ICE.ScheduleChecks(checkList, m_ReloadConfig.Logger);

                                        m_attachRequestCandidates.Remove(recmsg.TransactionID);

                                        #region signaling

                                        // any succeeded pair?
                                        if (checkList.candidatePairs.Any(item => item.state == CandidatePairState.Succeeded))
                                        {
                                            // get all succeeded pairs
                                            List<CandidatePair> succeededPairs = checkList.candidatePairs.Where(item => item.state == CandidatePairState.Succeeded).ToList();

                                            // send nomination signal to peer
                                            bool sentSuccessfull = false;
                                            bool nominated;
                                            int counter = 0;

                                            foreach (CandidatePair pair in succeededPairs)
                                            {
                                                // simply nominate the first succeeded pair
                                                if (counter == 0)
                                                    nominated = true;
                                                else
                                                    nominated = false;

                                                switch (pair.localCandidate.tcpType)
                                                {
                                                    case TcpType.Active:
                                                        {
                                                            if (pair.localCandidate.activeConnectingSocket != null)
                                                            {
                                                                sentSuccessfull = ICE.SendSignal(pair.localCandidate.activeConnectingSocket, nominated);
                                                                pair.nominated = nominated;
                                                            }
                                                        }
                                                        break;

                                                    case TcpType.Passive:
                                                        {
                                                            if (pair.localCandidate.passiveAcceptedSocket != null)
                                                            {
                                                                sentSuccessfull = ICE.SendSignal(pair.localCandidate.passiveAcceptedSocket, nominated);
                                                                pair.nominated = nominated;
                                                            }
                                                        }
                                                        break;

                                                    case TcpType.SO:
                                                        {
                                                            if (pair.localCandidate.soAcceptedSocket != null)
                                                            {
                                                                sentSuccessfull = ICE.SendSignal(pair.localCandidate.soAcceptedSocket, nominated);
                                                                pair.nominated = nominated;
                                                            }

                                                            else if (pair.localCandidate.soConnectingSocket != null)
                                                            {
                                                                sentSuccessfull = ICE.SendSignal(pair.localCandidate.soConnectingSocket, nominated);
                                                                pair.nominated = nominated;
                                                            }
                                                        }
                                                        break;

                                                }   // switch

                                                counter++;

                                            }   // foreach


                                            if (sentSuccessfull)
                                            {

                                            }


                                        #endregion  // signaling


                                            // Start Server here, if a nominated pair exists
                                            if (checkList.candidatePairs.Any(item => item.nominated))
                                            {
                                                CandidatePair choosenPair = checkList.candidatePairs.First(item => item.nominated);

                                                // save connection here too?
                                                //GetForwardingAndLinkManagementLayer().SaveConnection(choosenPair);

                                                // get connection
                                                Socket socket = GetForwardingAndLinkManagementLayer().GetConnection(choosenPair);

                                                // StartReloadTLSServer
                                                GetForwardingAndLinkManagementLayer().StartReloadTLSServer(socket);
                                            } // if (any nominated)


                                        }   // if (any succeeded pair)

                                        // Close all sockets of all candidate pairs not nominated
                                        //for (int i = 0; i < checkList.candidatePairs.Count; i++)
                                        //    if ((!checkList.candidatePairs[i].nominated) || (checkList.candidatePairs[i].state != CandidatePairState.Succeeded))
                                        //        ICE.CloseAllCandidateSockets(checkList.candidatePairs[i].localCandidate);
                                    }

                                    #endregion  // ICE

                                }

                                // existing nat candidates to free?
                                if (localCandidates != null)
                                {
                                    // free all port mappings created by UPnP
                                    foreach (IceCandidate cand in localCandidates)
                                    {
                                        if (cand.cand_type == CandType.tcp_nat)
                                        {
                                            UPnP upnp = new UPnP();
                                            bool discovered = upnp.Discover(cand.rel_addr_port.ipaddr);

                                            if (discovered)
                                                upnp.DeletePortMapping(cand.addr_port.port, ProtocolType.Tcp);

                                        }
                                    }
                                }

                            }

                        }


                    }
                }
            }
            catch (Exception)
            {

                throw;
            }

        }

        public void reload_app_attach_inbound(ReloadMessage recmsg)
        {
            AppAttachReqAns req_answ = (AppAttachReqAns)recmsg.reload_message_body;
            NodeId OriginatorID = recmsg.OriginatorID;
            Node Originator = new Node(recmsg.OriginatorID, req_answ.ice_candidates);

            m_topology.routing_table.AddNode(Originator);
            m_topology.routing_table.SetNodeState(Originator.Id, NodeState.attached);

            //Proprietary --joscha
            string destination_overlay = null;
            string source_overlay = null;

            if (recmsg.forwarding_header.fw_options != null)
            {

                foreach (ForwardingOption option in recmsg.forwarding_header.fw_options)
                {
                    if (option.fwo_type == ForwardingOptionsType.destinationOverlay)
                    {
                        destination_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("{0} Message  for destinationOverlay=" + destination_overlay, recmsg.reload_message_body.RELOAD_MsgCode));
                    }
                    if (option.fwo_type == ForwardingOptionsType.sourceOverlay)
                    {
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

            if (source_overlay == m_ReloadConfig.OverlayName)
            {

                // Send ping to establish a physical connection
                Arbiter.Activate(m_DispatcherQueue,
                  new IterativeTask<Destination, PingOption>(new Destination(Originator.Id),
                  PingOption.direct, SendPing));
            }
            if (req_answ != null && req_answ.ice_candidates != null)
            {
                if (recmsg.IsRequest())
                {

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("transport.cs - reload_app_attach_inbound"));

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
                else
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("{0} <== {1} (not handled!!)", req_answ.RELOAD_MsgCode.ToString().PadRight(16, ' '), OriginatorID));
                }
            }
        }

        private void reload_join_inbound(ReloadMessage recmsg)
        {
            JoinReqAns req_answ = (JoinReqAns)recmsg.reload_message_body;
            NodeId OriginatorID = recmsg.OriginatorID;

            if (recmsg.IsRequest())
            {
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

                if (m_ReloadConfig.IsBootstrap && m_ReloadConfig.State != ReloadConfig.RELOAD_State.Joined)
                {
                    m_ReloadConfig.State = ReloadConfig.RELOAD_State.Joined;
                    m_machine.StateUpdates(ReloadConfig.RELOAD_State.Joined);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                      String.Format("Changed to joined state"));
                }
            }
            else
            {
            }
        }

        private void reload_update_inbound(ReloadMessage recmsg)
        {
            UpdateReqAns req_answ = (UpdateReqAns)recmsg.reload_message_body;
            NodeId OriginatorID = recmsg.OriginatorID;
            Boolean force_send_update = false;

            if (recmsg.IsRequest())
            {
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
                if (req_answ.Successors.Count > 0)
                {
                    m_topology.routing_table.GetNode(OriginatorID).Successors = req_answ.Successors;
                    m_topology.routing_table.GetNode(OriginatorID).Predecessors = req_answ.Predecessors;
                }

                if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joining)
                {
                    if (m_ReloadConfig.AdmittingPeer != null &&
                      OriginatorID == m_ReloadConfig.AdmittingPeer.Id)
                    {
                        if (!m_topology.routing_table.IsWaitForJoinAnsw(OriginatorID))
                        {
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

                // delete old entries in LeavingTable
                List<NodeId> expiredNodes = new List<NodeId>();
                foreach (KeyValuePair<NodeId, DateTime> entry in m_topology.routing_table.LeavingNodes)
                {
                    if (entry.Value.AddSeconds(300) < DateTime.Now)
                        expiredNodes.Add(entry.Key);
                }
                foreach (NodeId id in expiredNodes)
                    m_topology.routing_table.LeavingNodes.Remove(id);

            }
            else
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Incoming UpdateAns");
            }
        }

        /// <summary>
        /// Handles incoming StoreReq messages.
        /// </summary>
        /// <param name="recmg">The received RELOAD message</param>
        private void reload_store_inbound(ReloadMessage recmsg)
        {

            if (recmsg.IsRequest())
            {

                StoreReq storeRequest = (StoreReq)recmsg.reload_message_body;
                NodeId originatorID = recmsg.OriginatorID;
                List<StoreKindData> recStoreKindData;

                // TODO: For now add certificate to global PKC Store, but they are only temporarilly needed in validateDataSignature
                m_ReloadConfig.AccessController.SetPKCs(recmsg.security_block.Certificates);

                Boolean validRequest = true;

                recStoreKindData = storeRequest.StoreKindData;

                // validate data signature
                foreach (StoreKindData store_kind in recStoreKindData)
                {
                    foreach (StoredData sd in store_kind.Values)
                    {
                        if (!m_ReloadConfig.AccessController.validateDataSignature(storeRequest.ResourceId, store_kind.Kind, sd))
                        {
                            validRequest = false;
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "DATA SIGNATURE INVALID!! Store Failed!!");
                        }
                    }
                }

                if (storeRequest.Replica_number == 1)
                {
                    // find sender in my predecessors
                    int sender = m_topology.routing_table.Predecessors.IndexOf(recmsg.OriginatorID);

                    // sender not in my predecessors
                    if (sender < 0)
                        validRequest = false;

                    // we are able to perform validity checks
                    if (m_topology.routing_table.Predecessors.Count > 2)
                    {
                        if (!storeRequest.ResourceId.ElementOfInterval(m_topology.routing_table.Predecessors[sender + 1], m_topology.routing_table.Predecessors[sender], true))
                            // is the storing peer responsible for that resourceId?
                            validRequest = false;
                    }

                }

                // REPLICATEST
                if (validRequest)
                {
                    if (storeRequest.Replica_number == 1)
                    {
                        if (!m_topology.Replicas.Contains(storeRequest.ResourceId.ToString()))
                        {
                            m_topology.Replicas.Add(storeRequest.ResourceId.ToString());
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Add Ressource {0} as Replica", storeRequest.ResourceId.ToString()));
                        }
                    }

                    if (storeRequest.Replica_number == 0 && m_topology.Replicas.Contains(storeRequest.ResourceId.ToString()))
                    {
                        m_topology.Replicas.Remove(storeRequest.ResourceId.ToString());
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("My Predecessor left. I'm now responsible for data {0}. Remove replica", storeRequest.ResourceId.ToString()));
                    }

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                      String.Format("{0} ==> {1} via: {3:x20} TransId={2:x16}",
                      RELOAD_MessageCode.Store_Answer.ToString().PadRight(16, ' '),
                      originatorID, recmsg.TransactionID, recmsg.LastHopNodeId));

                    //recStoreKindData = storeRequest.StoreKindData;
                    foreach (StoreKindData store_kind in recStoreKindData)
                    {
                        m_topology.Store(storeRequest.ResourceId, store_kind);
                    }
                    /* It then sends a Store request to its successor in the neighbor
                     * table and to that peer's successor.
                     * 
                     * see RELOAD base -12 p.104
                     */
                    List<NodeId> replicaNodes = new List<NodeId>();

                    NodeId successor = m_topology.routing_table.GetApprovedSuccessor();
                    BigInt nextSuccsessor = successor + 1;
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
                    if (m_machine is GWMachine)
                    { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
                        if (storeAnswer.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id)
                        {
                            storeAnswer.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
                            ((GWMachine)m_machine).GateWay.interDomainPeer.Transport.receive_message(storeAnswer);
                        }
                        else
                            send(storeAnswer, nextHop);
                    }
                    else
                        send(storeAnswer, nextHop);

                    // REPLICATEST
                    // incoming store request is not a replication request
                    if (storeRequest.Replica_number == 0)
                    {
                        int numberReplicas = m_topology.routing_table.Successors.Count >= 2 ? 2 : m_topology.routing_table.Successors.Count; // Replica number is max 2
                        // send replica to all successors
                        for (int i = 0; i < numberReplicas; i++)
                        {
                            NodeId successorNode = m_topology.routing_table.Successors[i];
                            ReloadMessage replica = create_store_req(new Destination(successorNode), storeRequest.ResourceId, recStoreKindData, true);
                            send(replica, m_topology.routing_table.GetNode(successorNode));
                        }
                    }
                }
                else
                {
                    // Signature over data in Store Request was invalid. Respond with Error Message!
                    send(create_erro_reply(new Destination(recmsg.OriginatorID), RELOAD_ErrorCode.Error_Forbidden, "Invalid Data Signature", ++m_ReloadConfig.TransactionID),
                      m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
                }

            }
            // its a StoreAns
            else
            {
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
        private void reload_fetch_inbound(ReloadMessage recmsg)
        {

            ReloadMessage sendmsg;

            if (recmsg.IsRequest())
            {
                FetchReq fetchRequest = (FetchReq)recmsg.reload_message_body;
                NodeId originatorId = recmsg.OriginatorID;

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD,
                  String.Format("{0} ==> {1} TransId={2:x16}",
                    RELOAD_MessageCode.Fetch_Answer.ToString().PadRight(16, ' '),
                      originatorId, recmsg.TransactionID));

                // List of SignerIdentities of the data to be fetched
                List<SignerIdentity> signers = new List<SignerIdentity>();

                var fetchKindResponses = new List<FetchKindResponse>();
                foreach (StoredDataSpecifier specifier in fetchRequest.Specifiers)
                {
                    FetchKindResponse fetchKindResponse;
                    if (m_topology.Fetch(fetchRequest.ResourceId,
                        specifier, out fetchKindResponse))
                    {
                        fetchKindResponses.Add(fetchKindResponse);

                        // add certificate
                        foreach (StoredData data in fetchKindResponse.values)
                            if (!signers.Contains(data.Signature.Identity))
                                signers.Add(data.Signature.Identity);
                    }
                    else
                    {
                        sendmsg = create_erro_reply(new Destination(originatorId),
                          RELOAD_ErrorCode.Error_Not_Found,
                            "Topology: RessourceId not found", recmsg.TransactionID);
                        recmsg.PutViaListToDestination(sendmsg);
                        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));

                        return;
                    }
                }

                sendmsg = create_fetch_answ(new Destination(originatorId), recmsg.TransactionID, fetchKindResponses);

                // get certificates for this data
                List<GenericCertificate> certs = new List<GenericCertificate>();
                certs.AddRange(m_ReloadConfig.AccessController.GetPKCs(signers));

                // add certificates to fetch answer
                sendmsg.security_block.Certificates.AddRange(certs);

                recmsg.PutViaListToDestination(sendmsg);
                //sendmsg.addOverlayForwardingOptions(recmsg);  //Proprietary  //--Joscha	

                if (m_machine is GWMachine)
                { //workaround in case date is stored at the gateway node responsible to route the message back into the interconnectionoverlay 
                    if (sendmsg.forwarding_header.destination_list[0].destination_data.node_id == ((GWMachine)m_machine).GateWay.interDomainPeer.Topology.LocalNode.Id)
                    {
                        sendmsg.reload_message_body.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Answer;
                        ((GWMachine)m_machine).GateWay.interDomainPeer.Inject(sendmsg); //TODO: change other cases
                    }
                    else
                        send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
                }
                else
                    send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
            }
            else
            {
                // It is a FetchAns, no reply needed
            }
        }

        private void reload_ping_inbound(ReloadMessage recmsg)
        {
            PingReqAns req_answ = (PingReqAns)recmsg.reload_message_body;
            NodeId originatorID = recmsg.OriginatorID;

            if (recmsg.IsRequest())
            {
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

                ReloadMessage sendmsg = create_ping_answ(new Destination(originatorID), recmsg.TransactionID);

                recmsg.PutViaListToDestination(sendmsg);

                send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
            }
            else
            {
            }
        }

        private void reload_leave_inbound(ReloadMessage recmsg)
        {
            LeaveReqAns req_answ = (LeaveReqAns)recmsg.reload_message_body;
            NodeId OriginatorID = recmsg.OriginatorID;

            if (req_answ.RELOAD_MsgCode == RELOAD_MessageCode.Leave_Request)
            {

                // Leaving nodes will be stored for 2 minutes to make sure we do not learn about them again
                m_topology.routing_table.AddLeavingNode(req_answ.LeavingNode);

                m_topology.InboundLeave(req_answ.LeavingNode);
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} ==> {1} TransId={2:x16}",
                    RELOAD_MessageCode.Leave_Answer.ToString().PadRight(16, ' '), OriginatorID, recmsg.TransactionID));

                ReloadMessage sendmsg = create_leave_answ(new Destination(OriginatorID), recmsg.TransactionID);
                recmsg.PutViaListToDestination(sendmsg);

                send(sendmsg, m_topology.routing_table.GetNode(recmsg.LastHopNodeId));
            }
            else
            {
            }
        }

        public void receive_message(ReloadMessage reloadMsg)
        {
            if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Exit)
                return;

            try
            {
                if (reloadMsg == null)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message: reloadMsg = null!!");
                    return;
                }

                if (reloadMsg.IsFragmented() && reloadMsg.IsSingleFragmentMessage() == false)
                {  // -- joscha
                    ReloadMessage reassembledMsg = null;
                    lock (fragmentedMessageBuffer)
                    {
                        reassembledMsg = reloadMsg.ReceiveFragmentedMessage(ref fragmentedMessageBuffer);
                    }
                    if (reassembledMsg == null) //not yet all fragments received => not reassembled
                        return;
                    else
                        reloadMsg = reassembledMsg; //message reassembled => continue as usual
                }

                //is this a request to be forwarded?
                if (!m_forwarding.ProcessMsg(reloadMsg))
                {
                    /* First of all, validate message */
                    if (!m_ReloadConfig.AccessController.RequestPermitted(reloadMsg))
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                          "Transport.receive_message(): " + reloadMsg.reload_message_body.RELOAD_MsgCode.ToString() + " Request originator cannot be validated!");
                        return;
                    }

                    /* handle only incoming RELOAD requests here...,
                     * all answers will be handled in the appropriate tasks
                     */
                    if (reloadMsg.IsRequest())
                    {
                        //message for local node
                        if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Store_Request)
                        {
                            StoreReq storeRequest = (StoreReq)reloadMsg.reload_message_body;
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16} Replicanumber={3}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadMsg.OriginatorID, reloadMsg.TransactionID, storeRequest.Replica_number));
                        }
                        else
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, String.Format("{0} <== {1} TransId={2:x16}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadMsg.OriginatorID, reloadMsg.TransactionID));

                        if (reloadMsg.forwarding_header.via_list != null)
                        {
                            try
                            {
                                foreach (Destination destx in reloadMsg.forwarding_header.via_list)
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Via={0} ", destx.ToString()));
                            }
                            catch (Exception ex)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message, ReloadMsg viaList" + ex.Message);
                            }
                        }
                        if (reloadMsg.forwarding_header.destination_list != null)
                        {
                            try
                            {
                                foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                                    if (desty.type != DestinationType.node || reloadMsg.forwarding_header.destination_list.Count > 1)
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Dest={0} ", desty.ToString()));
                            }
                            catch (Exception ex)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message, ReloadMsg destList" + ex.Message);
                            }
                        }

                        switch (reloadMsg.reload_message_body.RELOAD_MsgCode)
                        {
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
                                reload_ping_inbound(reloadMsg);
                                break;
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
                            case RELOAD_MessageCode.Error:
                                {
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
                    else
                    {

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

                        if (!ReloadGlobals.UseNoIce)     // markus
                        {
                            // we use ICE
                            if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Attach_Answer)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Attach Answer Transaction ID: {0:x}", reloadMsg.TransactionID));

                                // forward attach answer
                                reload_attach_inbound(reloadMsg);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "receive_message: " + ex.Message);
                ReloadGlobals.PrintException(m_ReloadConfig, ex);
                //System.Diagnostics.Debugger.Break();
            }
        }

        #region Creates Messages

        private ReloadMessage create_reload_message(Destination destination,
          UInt64 trans_id, RELOAD_MessageBody reload_content)
        {
            // markus
            if (reload_content.RELOAD_MsgCode == RELOAD_MessageCode.Attach_Request)
            {
                try
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Add candidates to Dictionary: Transaction ID: {0:x}", trans_id));
                    foreach (IceCandidate cand in ((AttachReqAns)reload_content).ice_candidates)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Add candidate: {0}:{1}", cand.addr_port.ipaddr.ToString(), cand.addr_port.port));

                    if (!m_attachRequestCandidates.ContainsKey(trans_id)) // because retry uses same transid
                        m_attachRequestCandidates.Add(trans_id, ((AttachReqAns)reload_content).ice_candidates);
                }
                catch (Exception)
                {

                    throw;
                }
            }
            // markus end

            return new ReloadMessage(m_ReloadConfig,
              m_topology.LocalNode.Id, destination, trans_id, reload_content);



        }

        public ReloadMessage create_reload_message(ReloadMessage reloadRequest)
        {
            try
            {
                reloadRequest.PutViaListToDestination();
            }
            catch (Exception ex)
            {
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
          Boolean fForceSendUpdate)
        {
            //return create_reload_message(destination, ++m_ReloadConfig.TransactionID,     // markus: commented out
            //    new AttachReqAns(m_topology.LocalNode, true, fForceSendUpdate));

            bool gatherActiveHostOnly = false;

            if (destination == new Destination(new ResourceId(m_topology.LocalNode.Id + (byte)1)))
                gatherActiveHostOnly = true;



            else
            {
                foreach (BootstrapServer bss in m_machine.BootstrapServer)
                {
                    var test = new Destination(new ResourceId(bss.NodeId));
                    if (destination == new Destination(new ResourceId(bss.NodeId + (byte)1)))
                    {
                        gatherActiveHostOnly = true;
                        break;
                    }
                }

                //if (!gatherActiveHostOnly)
                //{
                //    foreach (FTEntry ftentry in m_topology.routing_table.FingerTable)
                //    {
                //        var test = new Destination(ftentry.Finger);
                //        if (destination == new Destination(ftentry.Finger))
                //        {
                //            gatherActiveHostOnly = true;
                //            break;
                //        }
                //    }
                //}
            }

            // markus
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
                new AttachReqAns(m_topology.LocalNode, true, fForceSendUpdate, m_ReloadConfig.IsBootstrap, gatherActiveHostOnly));

        }

        public ReloadMessage create_app_attach_req(Destination destination)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new AppAttachReqAns(m_topology.LocalNode, true));
        }

        public ReloadMessage create_attach_answ(Destination destination, UInt64 trans_id)
        {
            //return create_reload_message(destination, trans_id, new AttachReqAns(m_topology.LocalNode, false, true));

            //return create_reload_message(destination, trans_id,       // markus: commented out
            //  new AttachReqAns(m_topology.LocalNode, false, false));

            // markus
            return create_reload_message(destination, trans_id,
                new AttachReqAns(m_topology.LocalNode, false, false, m_ReloadConfig.IsBootstrap, false));     // bootstrap doesnt send attach requests => attach answer never goes to bootstrap => 4th parameter false
        }

        public ReloadMessage create_app_attach_answ(Destination destination, UInt64 trans_id)
        {
            return create_reload_message(destination, trans_id, new AppAttachReqAns(m_topology.LocalNode, false));
        }

        public ReloadMessage create_join_req(Destination destination)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new JoinReqAns(m_topology.LocalNode, true));
        }

        public ReloadMessage create_join_answ(Destination destination, UInt64 trans_id)
        {
            return create_reload_message(destination, trans_id, new JoinReqAns(null, false));
        }

        public ReloadMessage create_update_req(Destination destination,
          TopologyPlugin.RoutingTable rt, ChordUpdateType type)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
              new UpdateReqAns(rt.GetApproved(rt.Successors), rt.GetApproved(rt.Predecessors),
                type, m_ReloadConfig.StartTime));
        }

        public ReloadMessage create_update_answ(Destination destination, UInt64 trans_id, RELOAD_ErrorCode result)
        {
            return create_reload_message(destination, trans_id, new UpdateReqAns(result));
        }

        public ReloadMessage create_leave_req(Destination destination)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new LeaveReqAns(m_topology.LocalNode, true));
        }

        public ReloadMessage create_leave_answ(Destination destination, UInt64 trans_id)
        {
            return create_reload_message(destination, trans_id, new LeaveReqAns(m_topology.LocalNode, false));
        }

        public ReloadMessage create_ping_req(Destination destination)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID, new PingReqAns(0, true));
        }

        public ReloadMessage create_ping_answ(Destination destination, UInt64 trans_id)
        {
            Random rand = new Random();
            return create_reload_message(destination, trans_id, new PingReqAns((UInt64)rand.Next(int.MinValue, int.MaxValue), false));
        }

        /// <summary>
        /// Creates a StoreReq according to RELOAD base -12 p.86
        /// </summary>
        /// <param name="destination">The store destination</param>
        /// <returns>A complete RELOAD StoreReq message including all headers</returns>
        public ReloadMessage create_store_req(Destination destination, List<StoreKindData> stored_kind_data, bool replica)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
                                         new StoreReq(destination.destination_data.ressource_id,
                                                      stored_kind_data,
                                                      m_machine.UsageManager, replica));
        }

        /// <summary>
        /// Creates a StoreReq that can be directed a specific NodeId 
        /// </summary>
        /// <param name="destination">The store destination NodeId</param>
        /// <param name="resourceId">The Id of the resource to store</param>
        /// <returns>A complete RELOAD StoreReq message including all headers</returns>
        public ReloadMessage create_store_req(Destination destination, ResourceId resourceId, List<StoreKindData> stored_kind_data, bool replica)
        {
            return create_reload_message(destination, ++m_ReloadConfig.TransactionID,
                                         new StoreReq(resourceId,
                                                      stored_kind_data,
                                                      m_machine.UsageManager, replica));
        }


        /// <summary>
        /// Creates a StoreAns message accoriding to RELOAD base -12 p.90
        /// </summary>
        /// <param name="destination">The answer destination address</param>
        /// <param name="trans_id">The transaction ID corresponding to the StoreReq</param>
        /// <param name="usage">The Usage data corresponding to the StoreReq</param>
        /// <returns></returns>
        public ReloadMessage create_store_answ(Destination dest,
          UInt64 trans_id, List<StoreKindData> skd, List<NodeId> replicas)
        {
            return create_reload_message(dest, trans_id, new StoreAns(skd, replicas));
        }

        /// <summary>
        /// Creates a FetchReq using the given StoredDataSpecifiers
        /// </summary>
        /// <param name="destination">The destination</param>
        /// <param name="specifiers">The StoredDataSpecifiers</param>
        /// <returns>A FetchReq</returns>
        private ReloadMessage create_fetch_req(Destination destination, List<StoredDataSpecifier> specifiers)
        {
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
        private ReloadMessage create_fetch_answ(Destination destination, UInt64 trans_id, List<FetchKindResponse> fetchKindResponses)
        {
            return create_reload_message(destination, trans_id, new FetchAns(fetchKindResponses, m_machine.UsageManager));
        }

        public ReloadMessage create_erro_reply(Destination destination, RELOAD_ErrorCode error, string errmsg, UInt64 trans_id)
        {
            return create_reload_message(destination, trans_id, new ErrorResponse(error, errmsg));
        }

        #endregion

        internal void send(ReloadMessage reloadMsg, Node NextHopNode)
        {

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
