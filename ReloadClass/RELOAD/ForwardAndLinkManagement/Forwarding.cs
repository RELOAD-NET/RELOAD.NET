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

#region Fowarding
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.ForwardAndLinkManagement {
  public class ForwardingLayer {
    private MessageTransport m_transport = null;
    private TopologyPlugin m_topology = null;
    private IForwardLinkManagement m_flm = null;
    private ReloadConfig m_ReloadConfig = null;

    private Port<UInt64> m_loopedTransactions;
    public Port<UInt64> LoopedTrans {
      get { return m_loopedTransactions; }
      set { m_loopedTransactions = value; }
    }

    public ForwardingLayer(Machine machine) {
      m_transport = machine.Transport;
      m_topology = machine.Topology;
      m_flm = machine.Interface_flm;
      m_ReloadConfig = machine.ReloadConfig;      
    }

    /// <summary>
    /// Checks if a inbound message must be forwarded
    /// 
    /// Returns true, if it will be forwarded
    /// </summary>
    /// <param name="reloadMsg">The inbound msg</param>
    /// <returns>true, if the message will be forwarded</returns>
    public bool ProcessMsg(ReloadMessage reloadMsg) {
      if (reloadMsg.OriginatorID == m_topology.LocalNode.Id) {
        if(m_loopedTransactions != null)
          m_loopedTransactions.Post(reloadMsg.TransactionID);
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
          String.Format("Looped back and dropped {0} <== {1} TransId={2:x16}",
          reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
          reloadMsg.OriginatorID, reloadMsg.TransactionID));
        lock ("print via") {
          if(reloadMsg.forwarding_header.via_list != null)
            foreach (Destination via in reloadMsg.forwarding_header.via_list)
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                String.Format("Via: {0}", via));
        }
        return true;
      }

      //check for validity
      if (reloadMsg.forwarding_header.destination_list == null ||
          reloadMsg.forwarding_header.destination_list.Count == 0) {
        //empty destination list, reply with error
        m_transport.send(m_transport.create_erro_reply(
          new Destination(m_topology.LocalNode.Id),
            RELOAD_ErrorCode.Error_Unsupported_Forwarding_Option,
            "Empty destination list", ++m_ReloadConfig.TransactionID),
            m_topology.routing_table.GetNode(reloadMsg.LastHopNodeId));
        return true;
      }


    NextDestination:

      //check first entry on destination list
      Destination dest = reloadMsg.forwarding_header.destination_list[0];

      if (reloadMsg.forwarding_header.via_list != null) {
        // check for a remarkable lenght of via headers, they should not exceed a special value (MAX_VIA_LIST_ENTRIES)
        if (reloadMsg.forwarding_header.via_list.Count > ReloadGlobals.MAX_VIA_LIST_ENTRIES) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
             String.Format("==> maximum via list length exceeded {0}: {1} from {2} Dest={3} TransID={4:x16}",
             ReloadGlobals.MAX_VIA_LIST_ENTRIES,
             reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
             reloadMsg.OriginatorID, dest.ToString(), reloadMsg.TransactionID));

          if (reloadMsg.forwarding_header.via_list != null) {
            foreach (Destination destx in reloadMsg.forwarding_header.via_list)
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                String.Format("Via={0} ", destx.ToString()));
          }
          if (reloadMsg.forwarding_header.destination_list != null) {
            foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
              if (desty.type != DestinationType.node ||
                  reloadMsg.forwarding_header.destination_list.Count > 1)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                  String.Format("    Dest={0} ", desty.ToString()));
          }
          return true;
        }
      }

      switch (dest.type) {
        case DestinationType.node:

          NodeId DestinationId = dest.destination_data.node_id;

          /* some loop checks first */
          if (m_topology.LocalNode.Id == reloadMsg.OriginatorID) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
              String.Format("==> Suspicious: packet looped back to me: {0} from {1} Dest={2} TransID={3:x16}",
              reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              reloadMsg.OriginatorID, dest.ToString(), reloadMsg.TransactionID));
          }

          if (DestinationId == reloadMsg.OriginatorID) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("Loop Warning: Node {0} tries to attach to itself, last hop={1} dropping request TransID={2:x16}", reloadMsg.OriginatorID, reloadMsg.LastHopNodeId, reloadMsg.TransactionID));
            if (reloadMsg.forwarding_header.via_list != null) {
              foreach (Destination destx in reloadMsg.forwarding_header.via_list)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Via={0} ", destx.ToString()));
            }
            if (reloadMsg.forwarding_header.destination_list != null) {
              foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                if (desty.type != DestinationType.node || reloadMsg.forwarding_header.destination_list.Count > 1)
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                    String.Format("    Dest={0} ", desty.ToString()));
            }
            return true;
          }

          if (reloadMsg.forwarding_header.via_list != null) {
            foreach (Destination destination in reloadMsg.forwarding_header.via_list) {
              if (destination.type == DestinationType.node &&
                destination.destination_data.node_id == DestinationId) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                  String.Format("==> packet looped back to me I'm already in via list: {0} from {1} Dest={2} TransID={3:x16}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadMsg.OriginatorID, dest.ToString(), reloadMsg.TransactionID));
                if (reloadMsg.forwarding_header.via_list != null) {
                  foreach (Destination destx in reloadMsg.forwarding_header.via_list)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Via={0} ", destx.ToString()));
                }
                if (reloadMsg.forwarding_header.destination_list != null) {
                  foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
                    if (desty.type != DestinationType.node || reloadMsg.forwarding_header.destination_list.Count > 1)
                      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Dest={0} ", desty.ToString()));
                }
              }
            }
          }

          //is this this node?
          if (DestinationId == m_topology.LocalNode.Id || DestinationId == ReloadGlobals.WildcardNodeId) {
            //one single entry only?
            if (reloadMsg.forwarding_header.destination_list.Count == 1)
              //message for local node
              return false;
            else {
              //remove local node from destination list
              reloadMsg.RemoveFirstDestEntry();
              goto NextDestination;
            }
          }

          Node NextHopNode = null;

          foreach (ReloadConnectionTableInfoElement rce in m_flm.ConnectionTable) {
            if (rce.NodeID != null && rce.NodeID == DestinationId) {
              NextHopNode = m_topology.routing_table.GetNode(DestinationId);
              break;
            }
          }

          /* Do we have ice candidates already from destination?  */
          if (NextHopNode == null)
            if (m_topology.routing_table.IsAttached(DestinationId))
              NextHopNode = m_topology.routing_table.GetNode(DestinationId);

          /* no direct physical parameters available? -> use routing */
          if (NextHopNode == null)
            /*  The Topology Plugin is responsible for maintaining the overlay
                algorithm Routing Table, which is consulted by the Forwarding and
                Link Management Layer before routing a message. 
             */

            NextHopNode = m_topology.routing_table.FindNextHopTo(
              dest.destination_data.node_id, true, false);

          if (NextHopNode == null || NextHopNode.Id == m_topology.LocalNode.Id) {
            if (reloadMsg.OriginatorID != m_topology.LocalNode.Id) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                String.Format("==> failed forwarding: {0} from {1} Dest={2}"+
                " TransID={3:x16}",
                reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
                reloadMsg.OriginatorID, dest.ToString(), reloadMsg.TransactionID));
              m_transport.send(m_transport.create_erro_reply(
                new Destination(reloadMsg.OriginatorID),
                RELOAD_ErrorCode.Error_Not_Found,
                "Node not found", ++m_ReloadConfig.TransactionID),
                m_topology.routing_table.GetNode(reloadMsg.LastHopNodeId));
            }
          }
          else {
            if (NextHopNode.Id == reloadMsg.OriginatorID) {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
                String.Format("Topo claims Originator responsible:"+
                " {0} from {1} Dest={2} TransID={3:x16}",
                reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
                reloadMsg.OriginatorID, dest.ToString(), reloadMsg.TransactionID));
            }

            if (reloadMsg.IsRequest()) {
              reloadMsg.AddViaHeader(m_topology.LocalNode.Id);
            }
            else
              reloadMsg.PutViaListToDestination();

            reloadMsg.LastHopNodeId = m_topology.LocalNode.Id;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING,
              String.Format("==> forwarding: {0} from {1} ==> {2}  Dest={3} TransID={4:x16}",
              reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
              reloadMsg.OriginatorID, NextHopNode.ToString(), dest.ToString(),
              reloadMsg.TransactionID));

            m_transport.send(reloadMsg, NextHopNode);
          }
          break;
        case DestinationType.resource: {
            DestinationId = new NodeId(dest.destination_data.ressource_id);
            NodeId test = new NodeId(HexStringConverter.ToByteArray(
              "464201D3D1BDA43AC047ECD75CE6201D"));
            if (DestinationId == test)
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, "");
            /*  The Topology Plugin is responsible for maintaining the overlay
                algorithm Routing Table, which is consulted by the Forwarding and
                Link Management Layer before routing a message. 
             */

            if (DestinationId == m_topology.LocalNode.Id) // || (m_topology.routing_table.Predecessors.Count > 0 &&
              //DestinationId.ElementOfInterval(m_topology.routing_table.Predecessors[0], m_ReloadConfig.LocalNodeID, false)))
              //message for local node
              return false;

            /* be carefull here, if this request looped back to us, exclude us from the possible forwarding targets
             */
            NextHopNode = m_topology.routing_table.FindNextHopTo(DestinationId, true, m_topology.LocalNode.Id == reloadMsg.OriginatorID);

            if (NextHopNode == null) {
              //we did not found another node responsible, so we are => message for local node
              return false;
            }
            else if (NextHopNode.Id == m_topology.LocalNode.Id) {
              //silently drop packet if there is more then one entry in destination list
              if (reloadMsg.forwarding_header.destination_list.Count > 1)
                break;
              //message for local node
              return false;
            }
            else {
              if (reloadMsg.IsRequest())
                reloadMsg.AddViaHeader(m_topology.LocalNode.Id);
              else
                reloadMsg.RemoveFirstDestEntry();

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING,
                String.Format("==> forwarding: {0} from {1} ==> {2} Dest={3} TransID={4:x16}",
                reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '),
                reloadMsg.OriginatorID, NextHopNode.ToString(), dest.ToString(),
                reloadMsg.TransactionID));
              /*  don't use create_reload* as it puts via header to destination   */
              m_transport.send(reloadMsg, NextHopNode);
            }
          }
          break;
        case DestinationType.compressed:
          //empty destination list, reply with error
          m_transport.send(m_transport.create_erro_reply(
            new Destination(reloadMsg.OriginatorID),
            RELOAD_ErrorCode.Error_Unsupported_Forwarding_Option,
            "Compressed destination type not supported",
            ++m_ReloadConfig.TransactionID),
            m_topology.routing_table.GetNode(reloadMsg.LastHopNodeId));
          break;
      }
      return true;
    }
  }

#endregion
}
