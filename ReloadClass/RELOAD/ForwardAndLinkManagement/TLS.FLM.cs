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
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Net;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace TSystems.RELOAD.ForwardAndLinkManagement
{

    public class ReloadFLM : IForwardLinkManagement
    {

        private DispatcherQueue m_DispatcherQueue = null;
        private ReloadConfig m_ReloadConfig;

        private OverlayLinkTLS link = new OverlayLinkTLS();

        // markus
        private Dictionary<IpAddressPort, Socket> m_connectionsToRemotePeer = new Dictionary<IpAddressPort, Socket>();

        public void StartReloadTLSServer(Socket socket)
        {
            // simply call InitReloadTLSServer
            link.InitReloadTLSServer(socket);
        }

        public void StartReloadTLSClient(NodeId nodeid, Socket socket)
        {
            // simply call InitReloadTLSClient
            ReloadSendParameters send_params = new ReloadSendParameters();

            link.InitReloadTLSClient(send_params, socket);

            if (send_params.connectionTableEntry != null)
            {
                ReloadConnectionTableEntry connectionTableEntry = send_params.connectionTableEntry; //TODO: not nice but does the trick
                if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                    nodeid = send_params.connectionTableEntry.NodeID;
            }
        }

        public void SaveConnection(CandidatePair choosenPair)
        {
            // get connection socket to remote peer
            switch (choosenPair.localCandidate.tcpType)
            {
                case TcpType.Active:
                    {
                        if (choosenPair.localCandidate.activeConnectingSocket.Connected)
                            m_connectionsToRemotePeer.Add(choosenPair.remoteCandidate.addr_port, choosenPair.localCandidate.activeConnectingSocket);
                    }
                    break;

                case TcpType.Passive:
                    {
                        if (choosenPair.localCandidate.passiveAcceptedSocket.Connected)
                            m_connectionsToRemotePeer.Add(choosenPair.remoteCandidate.addr_port, choosenPair.localCandidate.passiveAcceptedSocket);
                    }
                    break;

                case TcpType.SO:
                    {
                        if (choosenPair.localCandidate.soConnectingSocket.Connected)
                            m_connectionsToRemotePeer.Add(choosenPair.remoteCandidate.addr_port, choosenPair.localCandidate.soConnectingSocket);

                        else if (choosenPair.localCandidate.soAcceptedSocket.Connected)
                            m_connectionsToRemotePeer.Add(choosenPair.remoteCandidate.addr_port, choosenPair.localCandidate.soAcceptedSocket);
                    }
                    break;
            } // switch


        }

        public Socket GetConnection(CandidatePair choosenPair)
        {
            // get connection socket to remote peer
            switch (choosenPair.localCandidate.tcpType)
            {
                case TcpType.Active:
                    {
                        if (choosenPair.localCandidate.activeConnectingSocket.Connected)
                            return choosenPair.localCandidate.activeConnectingSocket;
                    }
                    break;

                case TcpType.Passive:
                    {
                        if (choosenPair.localCandidate.passiveAcceptedSocket.Connected)
                            return choosenPair.localCandidate.passiveAcceptedSocket;
                    }
                    break;

                case TcpType.SO:
                    {
                        if (choosenPair.localCandidate.soConnectingSocket.Connected)
                            return choosenPair.localCandidate.soConnectingSocket;

                        else if (choosenPair.localCandidate.soAcceptedSocket.Connected)
                            return choosenPair.localCandidate.soAcceptedSocket;
                    }
                    break;
            } // switch

            return null;
        }
        // markus end


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

        private Util.ThreadSafeDictionary<IceCandidate, ReloadSendParameters> connectionQueue = new Util.ThreadSafeDictionary<IceCandidate, ReloadSendParameters>();

        public Util.ThreadSafeDictionary<IceCandidate, ReloadSendParameters> GetConnectionQueue()
        {
            return connectionQueue;
        }

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
                            if (isSender && !m_ReloadConfig.IamClient ||
                                (m_ReloadConfig.AdmittingPeer != null && m_ReloadConfig.AdmittingPeer.Id != connectionTableEntry.NodeID))
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FLM: Close connection to {0}", association.AssociatedSocket.RemoteEndPoint));
                                //association.TLSClose(true);
                                association.AssociatedSslStream.Close();
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

        public IEnumerator<ITask> Send(Node node, ReloadMessage reloadMessage)  // node is remote peer
        {
            ReloadConnectionTableEntry connectionTableEntry = null;
            List<Byte[]> ByteArrayList = new List<byte[]>();
            ByteArrayList.Add(reloadMessage.ToBytes());
            //if (ReloadGlobals.FRAGMENTATION == true) {
            if (reloadMessage.forwarding_header.length > ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP)
            {
                if (ByteArrayList[0].Length > ReloadGlobals.FRAGMENT_SIZE)
                { //TODO: fragment size should not be fix
                    ByteArrayList.Clear();
                    ByteArrayList = reloadMessage.ToBytesFragmented(ReloadGlobals.FRAGMENT_SIZE);
                }
            }

            foreach (byte[] ByteArray in ByteArrayList)
            {
                /* Try to find a matching connection using node id or target address*/
                if (node.Id != null)
                {
                    connectionTableEntry = connectionTable.lookupEntry(node.Id);
                    if (connectionTableEntry != null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("FLM: Found open connection for target node id {0}", node.Id));
                }
                if (connectionTableEntry == null)
                {
                    // connectionTableEntry == null   => no connection to remote peer
                    List<IceCandidate> icecandidates = node.IceCandidates;    // remote peer candidates
                    if (icecandidates != null)
                    {
                        foreach (IceCandidate candidate in icecandidates)
                        {
                            switch (candidate.addr_port.type)
                            {
                                case AddressType.IPv6_Address:
                                case AddressType.IPv4_Address:
                                    {
                                        ReloadSendParameters send_params;

                                        // check if there is already a attempt to open a connection to this node
                                        //if (connectionQueue.ContainsKey(candidate.addr_port))
                                        if (connectionQueue.ContainsKey(candidate))
                                        {
                                            // here we have a connection attempt
                                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Connection Pending!!!!!!!!!!!!!!!!!!!!!!!!!!!"));
                                            //send_params = connectionQueue[candidate.addr_port];
                                            send_params = connectionQueue[candidate];
                                            yield return Arbiter.Receive(false, send_params.done, x => { });
                                            send_params.done.Post(true); //maybe there are still some other pending connections?

                                            if (send_params.connectionTableEntry != null)   //do we have a valid connection?
                                            {
                                                // here we have a valid connection
                                                connectionTableEntry = send_params.connectionTableEntry;
                                                if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                                                    node.Id = send_params.connectionTableEntry.NodeID;

                                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("DONE!!!!!!!!!!!!!!!!!!!!!!!!!!!"));
                                                //connectionQueue.Remove(candidate.addr_port);
                                                connectionQueue.Remove(candidate);

                                                if (node.Id != null)
                                                {
                                                    connectionTableEntry = connectionTable.lookupEntry(node.Id);
                                                    if (connectionTableEntry != null)
                                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("FLM: Found open connection for target node id {0}", node.Id));

                                                    send_params = new ReloadSendParameters()
                                                    {
                                                        connectionTableEntry = connectionTableEntry,
                                                        destinationAddress = null,
                                                        port = 0,
                                                        buffer = ByteArray,
                                                        frame = true,
                                                        done = new Port<bool>(),
                                                        // markus
                                                        connectionSocket = null,
                                                    };

                                                    yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));     // SEND
                                                    break;
                                                }
                                            }
                                            // here we have no valid connection, but a connection attempt (maybe break?)
                                            //break;    // markus
                                        }
                                        // no connection attempt (maybe else here?)
                                        // here is no existing connection and no attempt => try to connect

                                        // NO ICE or ICE but bootstrap
                                        if (ReloadGlobals.UseNoIce || candidate.cand_type == CandType.tcp_bootstrap)
                                        {
                                            connectionTableEntry = connectionTable.lookupEntry(new IPEndPoint(candidate.addr_port.ipaddr, candidate.addr_port.port));
                                            if (connectionTableEntry != null)
                                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FLM: Found open connection for target IP {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));
                                            else
                                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("Opening connection to {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));

                                            send_params = new ReloadSendParameters()
                                            {

                                                connectionTableEntry = connectionTableEntry,
                                                destinationAddress = candidate.addr_port.ipaddr,
                                                port = candidate.addr_port.port,
                                                buffer = ByteArray,
                                                frame = true,
                                                done = new Port<bool>(),
                                                // markus
                                                connectionSocket = null,
                                            };

                                            //send_params.done.Post(false);

                                            //connectionQueue[candidate.addr_port] = send_params;
                                            connectionQueue[candidate] = send_params;
                                            //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Insert {0}:{1} into connectionQueue", new IPAddress(send_params.destinationAddress.Address).ToString(), send_params.port));

                                            yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));   // SEND

                                            //connectionQueue.Remove(candidate.addr_port);
                                            connectionQueue.Remove(candidate);
                                            if (send_params.connectionTableEntry != null)
                                            {
                                                connectionTableEntry = send_params.connectionTableEntry; //TODO: not nice but does the trick
                                                if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                                                    node.Id = send_params.connectionTableEntry.NodeID;
                                            }
                                        }

                                        // ICE peer
                                        //else
                                        //{

                                        //    connectionTableEntry = connectionTable.lookupEntry(new IPEndPoint(candidate.addr_port.ipaddr, candidate.addr_port.port));
                                        //    if (connectionTableEntry != null)
                                        //        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("FLM: Found open connection for target IP {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));
                                        //    else
                                        //        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("Opening connection to {0}:{1}", candidate.addr_port.ipaddr, candidate.addr_port.port));


                                        //    // try to get connection
                                        //    Socket connectionSocket = null;
                                        //    m_connectionsToRemotePeer.TryGetValue(candidate.addr_port, out connectionSocket);
                                        //    //if (connectionSocket == null)
                                        //    //    continue;  //versuche mit nächsten candidate

                                        //    send_params = new ReloadSendParameters()
                                        //    {

                                        //        connectionTableEntry = connectionTableEntry,
                                        //        destinationAddress = candidate.addr_port.ipaddr,
                                        //        port = candidate.addr_port.port,
                                        //        buffer = ByteArray,
                                        //        frame = true,
                                        //        done = new Port<bool>(),
                                        //        // markus
                                        //        connectionSocket = connectionSocket,
                                        //    };

                                        //    //send_params.done.Post(false);

                                        //    connectionQueue[candidate.addr_port] = send_params;

                                        //    yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));   // SEND

                                        //    connectionQueue.Remove(candidate.addr_port);
                                        //    if (send_params.connectionTableEntry != null)
                                        //    {
                                        //        connectionTableEntry = send_params.connectionTableEntry; //TODO: not nice but does the trick
                                        //        if (send_params.connectionTableEntry.NodeID != null)        //TODO: correct?
                                        //            node.Id = send_params.connectionTableEntry.NodeID;
                                        //    }
                                        //}

                                    }
                                    break;
                            }
                            //just support one ice candidate only here
                            break;
                        } // foreach ice candidate

                    }
                }
                else
                {
                    // connectionTableEntry != null   => existing connection to remote peer
                    ReloadSendParameters send_params = new ReloadSendParameters()
                    {
                        connectionTableEntry = connectionTableEntry,
                        destinationAddress = null,
                        port = 0,
                        buffer = ByteArray,
                        frame = true,
                        done = new Port<bool>(),
                    };

                    yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, link.Send));     // SEND
                }
            } // foreach ByteArray
            yield break;
        }   // Send


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
