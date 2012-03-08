/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012 Thomas Kluge <t.kluge@gmx.de> 
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
* Last edited by: Alex <alexander.knauf@gmail.com>
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Net;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.ForwardAndLinkManagement
{

    public class ReloadFLM : IForwardLinkManagement
    {

        private DispatcherQueue m_DispatcherQueue = null;
        private ReloadConfig m_ReloadConfig;

        private OverlayLinkTLS link = new OverlayLinkTLS();
        public ReloadFLM(ReloadConfig reloadConfig)
        {
            m_ReloadConfig = reloadConfig;
            m_DispatcherQueue = m_ReloadConfig.DispatcherQueue;
            link.ReloadFLMEventHandler += new ReloadFLMEvent(link_ReloadFLMEventHandler);
        }


        ReloadFLMEventArgs link_ReloadFLMEventHandler(object sender, ReloadFLMEventArgs args)
        {
            if (ReloadFLMEventHandler == null)
                throw new System.Exception("No ReloadFLMEventHandler installed");

            switch (args.Eventtype)
            {
                case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK:
                    if (args.Message != null &&
                        ReloadFLMEventHandler != null)
                        ReloadFLMEventHandler(sender, args);
                    break;
                case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED:
                    if (ReloadFLMEventHandler != null)
                        ReloadFLMEventHandler(sender, args);
                    break;
            }
            return args;
        }


        private ReloadConnectionTable connectionTable = new ReloadConnectionTable();

        /// <summary>
        /// The event
        /// </summary>
        public event ReloadFLMEvent ReloadFLMEventHandler;

        private Dictionary<IpAddressPort, ReloadSendParameters> connectionQueue = new Dictionary<IpAddressPort, ReloadSendParameters>();

        #region ConnectionTable
        /// <summary>
        /// TASK: Manage RELOAD connections.
        /// </summary>
        /// <returns></returns>
        private IEnumerator<ITask> ManageConnections()
        {
            while (m_ReloadConfig.State < RELOAD.ReloadConfig.RELOAD_State.Shutdown)
            {
                Port<DateTime> timeoutPort = new Port<DateTime>();
                m_DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0, 10000), timeoutPort);
                yield return Arbiter.Receive(false, timeoutPort, x => { });

                List<string> removeCandidates = new List<string>();
                lock (connectionTable)
                {
                    foreach (string connectionKey in connectionTable.Keys)
                    {
                        ReloadConnectionTableEntry connectionTableEntry = connectionTable[connectionKey];
                        IAssociation association = (IAssociation)connectionTableEntry.secureObject;
                        bool isSender = association is ReloadTLSClient;

                        if ((DateTime.Now - connectionTableEntry.LastActivity).TotalSeconds >= ReloadGlobals.CHORD_PING_INTERVAL + 30)
                        {
                            //don't kill connection to admitting peer as client and only kill outbound connections
                            if (  isSender && !m_ReloadConfig.IamClient ||
                                ( m_ReloadConfig.AdmittingPeer != null && m_ReloadConfig.AdmittingPeer.Id != connectionTableEntry.NodeID ))
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FLM: Close connection to {0}", association.AssociatedSocket.RemoteEndPoint));
                                association.TLSClose(true);
                                association.AssociatedSocket.Close();
                                association.InputBufferOffset = 0;  /* Help rx to terminate */
                                removeCandidates.Add(connectionKey);
                            }
                        }
                    }

                    /* Post process deletion */
                    foreach (string connectionKey in removeCandidates)
                    {
                        connectionTable.Remove(connectionKey);
                    }
                }
            }
        }


        /// <summary>
        /// Provides connection table info.
        /// </summary>
        /// <returns></returns>
        public List<ReloadConnectionTableInfoElement> ConnectionTable
        {
            get
            {
                List<ReloadConnectionTableInfoElement> result = new List<ReloadConnectionTableInfoElement>();
                lock (connectionTable)
                {
                    foreach (string key in connectionTable.Keys)
                    {
                        ReloadConnectionTableInfoElement reload_connection_info = new ReloadConnectionTableInfoElement();
                        ReloadConnectionTableEntry connectionTableEntry = connectionTable[key];
                        reload_connection_info.AssociatedSocket = ((IAssociation)connectionTableEntry.secureObject).AssociatedSocket;
                        reload_connection_info.RemainingUpTime = connectionTableEntry.LastActivity.AddSeconds(ReloadGlobals.CHORD_PING_INTERVAL + 30) - DateTime.Now;
                        reload_connection_info.NodeID = connectionTableEntry.NodeID;
                        reload_connection_info.LastActivity = connectionTableEntry.LastActivity;
                        result.Add(reload_connection_info);
                    }
                }
                return result;
            }
        }
        #endregion

        public bool Init()
        {
            bool result = link.Init(m_ReloadConfig, ref connectionTable);

            if (result)
                Arbiter.Activate(this.m_DispatcherQueue, Arbiter.FromIteratorHandler(ManageConnections));
            return result;
        }

        public IEnumerator<ITask> Send(Node node, ReloadMessage reloadMessage) {
          /* Try to find a matching connection using node id or target address*/
          ReloadConnectionTableEntry connectionTableEntry = null;
          List<Byte[]> ByteArrayList = new List<byte[]>();
          ByteArrayList.Add(reloadMessage.ToBytes());
          if (ReloadGlobals.FRAGMENTATION == true) {
            if (ByteArrayList[0].Length > ReloadGlobals.FRAGMENT_SIZE) { //TODO: fragment size should not be fix
              ByteArrayList.Clear();
              ByteArrayList = reloadMessage.ToBytesFragmented(ReloadGlobals.FRAGMENT_SIZE);
            }
          }

          foreach (byte[] ByteArray in ByteArrayList) {

            if (node.Id != null) {
              connectionTableEntry = connectionTable.lookupEntry(node.Id);
              if (connectionTableEntry != null)
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("FLM: Found open connection for target node id {0}", node.Id));
            }
            if (connectionTableEntry == null) {
              List<IceCandidate> icecandidates = node.IceCandidates;

              if (icecandidates != null) {
                foreach (IceCandidate candidate in icecandidates) {
                  switch (candidate.addr_port.type) {
                    case AddressType.IPv6_Address:
                    case AddressType.IPv4_Address: {
                        ReloadSendParameters send_params;

                        // check if there is already a atempt to open a connection to this node
                        if (connectionQueue.ContainsKey(candidate.addr_port)) {
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Connection Pending!!!!!!!!!!!!!!!!!!!!!!!!!!!"));
                          send_params = connectionQueue[candidate.addr_port];
                          yield return Arbiter.Receive(false, send_params.done, x => { });
                          send_params.done.Post(true); //maybe there are still some other pending connections?

                          if (send_params.connectionTableEntry != null) {   //do we have a valid connection?
                            connectionTableEntry = send_params.connectionTableEntry;
                            if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                              node.Id = send_params.connectionTableEntry.NodeID;

                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("DONE!!!!!!!!!!!!!!!!!!!!!!!!!!!"));
                            connectionQueue.Remove(candidate.addr_port);

                            if (node.Id != null) {
                              connectionTableEntry = connectionTable.lookupEntry(node.Id);
                              if (connectionTableEntry != null)
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("FLM: Found open connection for target node id {0}", node.Id));

                              send_params = new ReloadSendParameters() {
                                connectionTableEntry = connectionTableEntry,
                                destinationAddress = null,
                                port = 0,
                                buffer = ByteArray,
                                frame = true,
                                done = new Port<bool>(),
                              };

                              yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));
                              break;
                            }
                          }
                        }
                        connectionTableEntry = connectionTable.lookupEntry(new IPEndPoint(candidate.addr_port.ipaddr, candidate.addr_port.port));
                        if (connectionTableEntry != null)
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FLM: Found open connection for target IP {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));
                        else
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FOpening connection to {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));
                        send_params = new ReloadSendParameters() {

                          connectionTableEntry = connectionTableEntry,
                          destinationAddress = candidate.addr_port.ipaddr,
                          port = candidate.addr_port.port,
                          buffer = ByteArray,
                          frame = true,
                          done = new Port<bool>(),
                        };

                        //send_params.done.Post(false);

                        connectionQueue[candidate.addr_port] = send_params;

                        yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));

                        connectionQueue.Remove(candidate.addr_port);
                        if (send_params.connectionTableEntry != null) {
                          connectionTableEntry = send_params.connectionTableEntry; //TODO: not nice but does the trick
                          if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                            node.Id = send_params.connectionTableEntry.NodeID;
                        }
                      }
                      break;
                  }
                  //just support one ice candidate only here
                  break;
                }

              }
            }
            else {
              ReloadSendParameters send_params = new ReloadSendParameters() {
                connectionTableEntry = connectionTableEntry,
                destinationAddress = null,
                port = 0,
                buffer = ByteArray,
                frame = true,
                done = new Port<bool>(),
              };
              yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));
            }
          }
          yield break;
        }


        public bool NextHopInConnectionTable(NodeId dest_node_id)
        {
            return connectionTable.lookupEntry(dest_node_id) == null;
        }

        /// <summary>
        /// Shut downs listeners and receivers.
        /// </summary>
        /// <returns></returns>
        public void ShutDown()
        {
            link.ShutDown();
        }
    }
}
