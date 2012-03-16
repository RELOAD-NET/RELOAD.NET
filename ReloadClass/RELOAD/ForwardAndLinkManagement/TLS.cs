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
using System.Net;
using System.Net.Sockets;

using Microsoft.Ccr.Core;
using System.Text;
using System.Collections;
using System.Reflection;

using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

using SBSSLCommon;
using SBServer;
using SBUtils;
using SBX509;
using SBCustomCertStorage;
using SBClient;
using SBDTLSClient;
using SBPKCS10;

using TSystems.RELOAD;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.ForwardAndLinkManagement
{

    internal class OverlayLinkTLS
    {

        private Socket m_ListenerSocket = null;

        private DispatcherQueue m_DispatcherQueue = null;
        /// <summary>
        /// Connectiontable. Managed by the ForwardAndLinkManagement instance. Kept here as reference for quick updates.
        /// </summary>
        private ReloadConnectionTable m_connection_table;

        /// <summary>
        /// The event
        /// </summary>
        internal event ReloadFLMEvent ReloadFLMEventHandler;


        private ReloadConfig m_ReloadConfig = null;


        #region Tasks
        /// <summary>
        /// TASK: Socket listen and connection accept
        /// </summary>
        /// <param name="port">The port.</param>
        /// <returns></returns>
        private IEnumerator<ITask> linkListen(int port)
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);

            Socket ListenSocket = new Socket(localEndPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (ReloadGlobals.FRAGMENTATION == true)
                ListenSocket.NoDelay = true; //--joscha no nagle
            ListenSocket.Bind(localEndPoint);
            ListenSocket.Listen(1024);

            m_ListenerSocket = ListenSocket;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("TLS_S: Waiting for connections on port {0}", port));

            while (m_ReloadConfig.State  < ReloadConfig.RELOAD_State.Shutdown)
            {
                var iarPort = new Port<IAsyncResult>();
                Socket associatedSocket = null;
                ListenSocket.BeginAccept(iarPort.Post, null);
                yield return Arbiter.Receive(false, iarPort, iar => 
                {
                    try
                    {
                        associatedSocket = ListenSocket.EndAccept(iar);
                    }
                    catch
                    {
                        associatedSocket = null;
                    }
                });
                
                /* Setup new TLS Server */
                ReloadTLSServer reload_server = new ReloadTLSServer(associatedSocket);
                if (ReloadGlobals.only_RSA_NULL_MD5 == true) {
                  for (short i = 0; i < 107; i++) {
                    reload_server.set_CipherSuites(i, false);
                  }
                  reload_server.set_CipherSuites(SBConstants.__Global.SB_SUITE_RSA_NULL_MD5, true);
                }
                //reload_client.Enabled = !ReloadGlobals.TLS_PASSTHROUGH; --joscha
                reload_server.Enabled = true;   //--joscha
                reload_server.ForceCertificateChain = false;
                reload_server.ClientAuthentication = true;
                reload_server.Versions = SBConstants.Unit.sbSSL2 | SBConstants.Unit.sbSSL3 | SBConstants.Unit.sbTLS1 | SBConstants.Unit.sbTLS11;
                reload_server.CertStorage = m_ReloadConfig.ReloadLocalCertStorage;
                reload_server.OnOpenConnection += new TSBOpenConnectionEvent(SBB_OnOpenConnection);
                reload_server.OnData += new TSBDataEvent(SBB_OnData);
                reload_server.OnSend += new TSBSendEvent(SBB_OnSend);
                reload_server.OnError += new TSBErrorEvent(SBB_OnError);
                reload_server.OnCertificateValidate += new TSBCertificateValidateEvent(SBB_OnCertificateValidate);
                reload_server.OnReceive += new TSBReceiveEvent(SBB_OnReceive);

                /* Add or update connection list */
                //TKTODO needed? m_connection_table.updateEntry(reload_server);
                reload_server.Open();
                if (ReloadGlobals.TLS_PASSTHROUGH) //hack for ReloadGlobals.TLS_PASSTHROUGH
                {
                    if (reload_server.Active == true) 
                    {
                        reload_server.Enabled = false;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_S: SSL DISABLED"));
                    }
                    else
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_S: SSL NOT DISABLED"));
                }
                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<object>(reload_server, linkReceive));
                if (associatedSocket != null)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_S: {0}, Accepted client {1}", reload_server.GetHashCode(), associatedSocket.RemoteEndPoint));
            }
        }

        /// <summary>
        /// TASK: Socket data reception for server and client part.
        /// </summary>
        /// <param name="secure_object">The secure_object.</param>
        /// <returns></returns>
        private IEnumerator<ITask> linkReceive(object secure_object)
        {
          if (ReloadGlobals.TLS_PASSTHROUGH && secure_object is ReloadTLSServer) //hack for ReloadGlobals.TLS_PASSTHROUGH
            {
                if (((ReloadTLSServer)secure_object).Active == true)
                {
                    ((ReloadTLSServer)secure_object).Enabled = false;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_linkReceive: SSL DISABLED"));
                }
                else
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_linkReceive: SSL NOT DISABLED"));
            }

            IAssociation association = (IAssociation)secure_object;
            while (m_ReloadConfig.State  < ReloadConfig.RELOAD_State.Exit)
            {
                var iarPort = new Port<IAsyncResult>();
                int bytesReceived = 0;

                association.AssociatedSocket.BeginReceive(
                    association.InputBuffer,
                    association.InputBufferOffset,
                    association.InputBuffer.Length - association.InputBufferOffset,
                    SocketFlags.None, iarPort.Post, null);
                
                yield return Arbiter.Receive(false, iarPort, iar => 
                {
                    try
                    {
                        bytesReceived = association.AssociatedSocket.EndReceive(iar);
                    }
                    catch
                    {
                        bytesReceived = 0;
                    }
                });
                    

                if (bytesReceived <= 0)
                {
                    HandleRemoteClosing(association.AssociatedSocket);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("linkReceive: {0}, connection closed", association.GetHashCode()));
                    break;
                }

                association.InputBufferOffset += bytesReceived;
                while (association.InputBufferOffset > 0)
                {
                    if (association.AssociatedSocket.Connected)
                        association.TLSDataAvailable();
                    else
                    {
                        HandleRemoteClosing(association.AssociatedSocket);
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("linkReceive: {0}, connection broken", association.GetHashCode()));
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// TASK: Send data to the SBB, connect socket if required
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private IEnumerator<ITask> linkSend(Node node, ReloadSendParameters send_params)
        {
            if (send_params.connectionTableEntry == null)
            {
                /* No open connection, open new connection */
                Socket socket = new Socket(send_params.destinationAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: Connect socket {0}, {1}:{2}", socket.Handle, send_params.destinationAddress, send_params.port));

                var iarPort = new Port<IAsyncResult>();
                socket.BeginConnect(new IPEndPoint(send_params.destinationAddress, send_params.port), iarPort.Post, null);

                bool connectError = false;
                yield return Arbiter.Receive(false, iarPort, iar =>
                {
                    try
                    {
                        socket.EndConnect(iar);
                    }
                    catch (Exception ex)
                    {
                        HandleRemoteClosing(node);
                        connectError = true;
                        m_ReloadConfig.Statistics.IncConnectionError();
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, ex.Message);
                        if (ReloadFLMEventHandler != null)
                            ReloadFLMEventHandler(this, new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED, null, null));

                    }
                });
                if (connectError) 
                {
                    send_params.done.Post(true);    //--joscha
                    yield break;
                }

                ReloadTLSClient reload_client = new ReloadTLSClient(socket);
                if (ReloadGlobals.only_RSA_NULL_MD5 == true) {
                  for (short i = 0; i < 107; i++) {
                    reload_client.set_CipherSuites(i, false);
                  }
                  reload_client.set_CipherSuites(SBConstants.__Global.SB_SUITE_RSA_NULL_MD5, true);
                }
                //reload_client = new ReloadTLSClient(socket);
                //reload_client.Enabled = !ReloadGlobals.TLS_PASSTHROUGH; --joscha
                reload_client.Versions = SBConstants.Unit.sbSSL2 | SBConstants.Unit.sbSSL3 | SBConstants.Unit.sbTLS1 | SBConstants.Unit.sbTLS11;
                reload_client.ClientCertStorage = m_ReloadConfig.ReloadLocalCertStorage;

                reload_client.OnOpenConnection += new SBSSLCommon.TSBOpenConnectionEvent(SBB_OnOpenConnection);
                reload_client.OnData += new SBSSLCommon.TSBDataEvent(SBB_OnData);
                reload_client.OnSend += new SBSSLCommon.TSBSendEvent(SBB_OnSend);
                reload_client.OnReceive += new SBSSLCommon.TSBReceiveEvent(SBB_OnReceive);
                reload_client.OnCertificateValidate += new SBSSLCommon.TSBCertificateValidateEvent(SBB_OnCertificateValidate);
                reload_client.OnError += new SBSSLCommon.TSBErrorEvent(SBB_OnError);

                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<object>(reload_client, linkReceive));
                reload_client.TLSConnectionOpen = new Port<bool>();

                reload_client.Open();

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: SBB client object {0}", reload_client.GetHashCode()));

                //if (!ReloadGlobals.TLS_PASSTHROUGH) --joscha
                {
                    bool isOpen = false;
                    yield return Arbiter.Receive(false, reload_client.TLSConnectionOpen, result => { isOpen = result; });

                    if (!isOpen)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_C: Error Timeout"));
                        if (ReloadFLMEventHandler != null)
                            ReloadFLMEventHandler(this, new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED, null, null));
                        send_params.done.Post(true); // --joscha
                        yield break;
                    }
                }

                /* Add/update connection list */
                send_params.connectionTableEntry = m_connection_table.updateEntry(reload_client);


                if (ReloadGlobals.TLS_PASSTHROUGH)//hack for ReloadGlobals.TLS_PASSTHROUGH
                {
                    if (reload_client.Active == true)
                    {
                        reload_client.Enabled = false;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_C: SSL DISABLED"));
                    }
                    else
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_C: SSL NOT DISABLED"));
                }
                if (ReloadGlobals.FRAGMENTATION == true)
                {
                    socket.NoDelay = true; //--joscha no nagle
                }
            }

            send_params.done.Post(true); // connection attempt for send_params is finished --joscha

            if (send_params.frame)
                send_params.buffer = addFrameHeader(send_params.connectionTableEntry, send_params.buffer);
  
            IAssociation association = ((IAssociation)send_params.connectionTableEntry.secureObject);
            if (association.TLSConnectionIsOpen)
            {
                association.TLSSendData(send_params.buffer);
                send_params.connectionTableEntry.LastActivity = DateTime.Now;    /* Re-trigger activity timer */
            }
            else
                association.TLSConnectionWaitQueue.Enqueue(send_params.buffer);
        }

        private void HandleRemoteClosing(Node node)
        {
            if (node != null && node.Id != null)
                m_ReloadConfig.ThisMachine.Transport.InboundClose(node.Id);
        }

        private void HandleRemoteClosing(Socket client)
        {
            lock (m_connection_table)
            {
                foreach (KeyValuePair<String, ReloadConnectionTableEntry> entry in m_connection_table)
                {
                    IAssociation association = (IAssociation)entry.Value.secureObject;
                    if (association.AssociatedSocket == client)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: lost connection to {0} cleaning connection table", entry));

                        m_connection_table.Remove(entry.Key);
                        m_ReloadConfig.ThisMachine.Transport.InboundClose(entry.Value.NodeID);
                        return;
                    }
                }
            }
        }
        #endregion

        #region SBB Eventhandlers
        private void SBB_OnOpenConnection(object Sender)
        {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TLS, String.Format("TLS_{0}: TLS connection opened", Sender is ReloadTLSServer ? "S" : "C"));
            IAssociation association = ((IAssociation)Sender);
            association.TLSConnectionIsOpen = true;

            if (Sender is ReloadTLSServer)//hack for ReloadGlobals.TLS_PASSTHROUGH
            {
                ReloadTLSServer server =  ((ReloadTLSServer)Sender);
                if (ReloadGlobals.TLS_PASSTHROUGH)
                {
                    if (server.Active == true)
                    {
                        server.Enabled = false;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_S SBB_OnOpenConnection: SSL DISABLED"));
                    }
                    else
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_S SBB_OnOpenConnection: SSL NOT DISABLED"));
                }
            }

            if (association.TLSConnectionOpen != null)
            {
                association.TLSConnectionOpen.Post(true);
            }

            foreach (object o in association.TLSConnectionWaitQueue)
            {
                byte[] b = (byte[])o;
                association.TLSSendData(b);
            }
        }

        /// <summary>
        /// SBB has some bytes for the user
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="Buffer">The buffer.</param>
        private void SBB_OnData(object Sender, byte[] Buffer)
        {
            try
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_{0}: Data received, len {1}", Sender is ReloadTLSServer ? "S" : "C", Buffer.Length));
                IAssociation association = (IAssociation)Sender;
                ReloadConnectionTableEntry connectionTableEntry = m_connection_table.updateEntry(Sender);
                if (ReloadFLMEventHandler != null)
                {
                    if (connectionTableEntry != null)
                    {
                        ReloadMessage reloadMsg = null;
                        byte[] AnalysedBuffer = null;
                        long bytesProcessed = 0;
                        BinaryReader reader = new BinaryReader(new MemoryStream(Buffer));
                        do { //TODO: optimize! Problem: streaming socket => no guarantee to receive only a single ReloadMessage in one SBB_OnData call also reception of partial messages possible (not handled so far)
                            Byte[] buf = new Byte[Buffer.Length - bytesProcessed];
                            reader.BaseStream.Seek(bytesProcessed, SeekOrigin.Begin);
                            reader.Read(buf, 0, (int)(Buffer.Length - bytesProcessed));
                            uint bytecount=0;
                            AnalysedBuffer = analyseFrameHeader(connectionTableEntry, buf, ref bytecount);
                            bytesProcessed += bytecount;    //framing header

                            if (AnalysedBuffer != null)
                            {
                                //bytesProcessed = 0;
                                long temp = bytesProcessed;
                                reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(AnalysedBuffer, ref temp, ReloadMessage.ReadFlags.full);
                                if (reloadMsg == null) {

                                }
                                 //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("MessageLength={0}", temp));
                                bytesProcessed += temp;
                                reloadMsg.LastHopNodeId = connectionTableEntry.NodeID;

                                ReloadFLMEventHandler(this,
                                    new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK, connectionTableEntry, reloadMsg));
                            }
                            else
                            {
                                if (buf != null && buf.Length > 500)
                                {
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("TLS_{0}: Data of length {1} inside thrown block!", Sender is ReloadTLSServer ? "S" : "C", Buffer.Length));
                                    //break;
                                }
                            }
                            //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("bytesProcessed={0}", bytesProcessed));
                        } while (bytesProcessed < Buffer.Length);
                        if (Buffer.Length != bytesProcessed)
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("TLS_{0}: Buffer.Length({1}) != bytesProcessed({2}) inside thrown block!", Sender is ReloadTLSServer ? "S" : "C", Buffer.Length, bytesProcessed));
                      }
                      else
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_{0}: connectionTableEntry == null! Data lost!", Sender is ReloadTLSServer ? "S" : "C"));
                }
                else
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("TLS_{0}: ReloadFLMEventHandler == null!", Sender is ReloadTLSServer ? "S" : "C"));
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SBB_OnData: " + ex.Message);
            }
        }

        /// <summary>
        /// Called if SBB needs some byte to consume
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="Buffer">The buffer.</param>
        /// <param name="MaxSize">Size of the max.</param>
        /// <param name="Written">The written.</param>
        private void SBB_OnReceive(object Sender, ref byte[] InputBuffer, int MaxSize, out int Written)
        {
            IAssociation association = (IAssociation)Sender;
            int len = Math.Min(MaxSize, association.InputBufferOffset);
            Written = len;
            association.InputBufferOffset -= len;

            m_ReloadConfig.Statistics.BytesRx = (ulong)len;

            if (len == 0)
            {
                association.InputBufferOffset = 0;
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SBB_OnReceive: Clearing association InputBuffer on MaxSize=0");
                return;
            }
            Buffer.BlockCopy(association.InputBuffer, 0, InputBuffer, 0, len);
            if (association.InputBufferOffset > 0)
                Buffer.BlockCopy(association.InputBuffer, len, association.InputBuffer, 0, association.InputBufferOffset);
        }


        /// <summary>
        /// SBB callback on request to send TLS data.
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="Buffer">The buffer.</param>
        private void SBB_OnSend(object Sender, byte[] Buffer)
        {
            IAssociation association = (IAssociation)Sender;
            try
            {
                Socket remoteSocket = association.AssociatedSocket;
                remoteSocket.BeginSend(Buffer, 0, Buffer.Length, 0, new AsyncCallback(socketAsyncSendCallback), association);
                m_ReloadConfig.Statistics.BytesTx = (ulong)Buffer.Length;
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                {
                    Socket remoteSocket = association.AssociatedSocket;
                    HandleRemoteClosing(remoteSocket);
                }
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SBB_OnSend: " + ex.Message);
            }
        }

        /// <summary>
        /// SBB callback for certificate validation.
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="X509Certificate">The X509 certificate.</param>
        /// <param name="Validate">if set to <c>true</c> [validate].</param>
        private void SBB_OnCertificateValidate(object Sender, TElX509Certificate X509Certificate, ref bool Validate)
        {
            string rfc822Name = null;

            NodeId nodeid = ReloadGlobals.retrieveNodeIDfromCertificate(X509Certificate, ref rfc822Name);

            Validate = X509Certificate.ValidateWithCA(m_ReloadConfig.CACertificate);

            if(!Validate)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_{0}: NodeID {1} Endpoint {2}, Certificate validation failed (CA Issuer {3})", Sender is ReloadTLSServer ? "S" : "C", nodeid, (Sender as IAssociation).AssociatedSocket.RemoteEndPoint.ToString(), X509Certificate.IssuerName.CommonName));
                return;
            }

            (Sender as IAssociation).RemoteNodeId = nodeid;

            ReloadConnectionTableEntry connectionTableEntry;

            // replaced lock more above. Got race conditions because of finger table attaches
            lock (m_connection_table)
            {

                connectionTableEntry = m_connection_table.lookupEntry(nodeid);

                if (connectionTableEntry == null)
                {
                    connectionTableEntry = new ReloadConnectionTableEntry() { secureObject = Sender, LastActivity = DateTime.Now };
                    connectionTableEntry.NodeID = nodeid;
                    m_connection_table.Add(nodeid.ToString(), connectionTableEntry);
                }
            }

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TLS, String.Format("TLS_{0}: Got NodeID {1}, set to {2}", Sender is ReloadTLSServer ? "S" : "C", connectionTableEntry.NodeID, (Sender as IAssociation).AssociatedSocket.RemoteEndPoint.ToString()));
        }

        /// <summary>
        /// SBB callback on error.
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="ErrorCode">The error code.</param>
        /// <param name="Fatal">if set to <c>true</c> [fatal].</param>
        /// <param name="Remote">if set to <c>true</c> [remote].</param>
        private void SBB_OnError(object Sender, int ErrorCode, bool Fatal, bool Remote)
        {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_{0}: Error {1}, {2}", Sender is ReloadTLSServer ? "S" : "C", ErrorCode, Remote?"remote":"local"));

            IAssociation association = (IAssociation)Sender;
            association.TLSClose(true);
            HandleRemoteClosing(association.AssociatedSocket);

            m_ReloadConfig.Statistics.IncConnectionError();
        }
        #endregion

        /// <summary>
        /// Socket async send callback.
        /// </summary>
        /// <param name="ar">The ar.</param>
        private void socketAsyncSendCallback(IAsyncResult ar)
        {
            IAssociation association = (IAssociation)ar.AsyncState;
            Socket remoteSocket = association.AssociatedSocket;
            try
            {
                remoteSocket.EndSend(ar);
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, "AsyncSend SocketError:" + ((SocketException)ex).ErrorCode.ToString());
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AsyncSend: " + ex.Message);
            }
        }

        #region Public functions
        /// <summary>
        /// Initialize TLS overlay link layer.
        /// </summary>
        /// <param name="certName">Name of the cert.</param>
        /// <param name="certPass">The cert pass.</param>
        /// <param name="port">The port.</param>
        /// <param name="nolisten">if set to <c>true</c> [nolisten].</param>
        /// <param name="connectionTable">The connection table.</param>
        /// <param name="receiveDelegate">The receive delegate.</param>
        /// <param name="sendDelegate">The send delegate.</param>
        internal bool Init(ReloadConfig reloadConfig, ref ReloadConnectionTable connectionTable)
        {
            /* Setup delegates and refs */
            this.m_DispatcherQueue = reloadConfig.DispatcherQueue;
            this.m_ReloadConfig = reloadConfig;
            this.m_connection_table = connectionTable;

            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<int>(m_ReloadConfig.ListenPort, linkListen));
            return true;
        }

        /// <summary>
        /// Send data.
        /// </summary>
        /// <param name="send_params">The send_params.</param>
        internal IEnumerator<ITask> Send(Node node, ReloadSendParameters send_params) 
        {
            yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, linkSend));
        }
        #endregion

        internal void ShutDown()
        {
            try
            {
                if (m_connection_table != null)
                    foreach (KeyValuePair<string, ReloadConnectionTableEntry> pair in m_connection_table)
                    {
                        Socket AssociatedSocket = ((IAssociation)pair.Value.secureObject).AssociatedSocket;
                        if (AssociatedSocket != null)
                        {
                            if (AssociatedSocket.Connected)
                                AssociatedSocket.Shutdown(SocketShutdown.Both);
                            AssociatedSocket.Close();
                        }
                    }

                if (m_ListenerSocket.Connected)
                    m_ListenerSocket.Shutdown(SocketShutdown.Both);
            }
            catch { };

            //this method calls dispose inside
            m_ListenerSocket.Close();
            //let CCR finish task to prevent ObjectDisposeException
            System.Threading.Thread.Sleep(250);
        }

        public FramedMessageData FMD_FromBytes(byte[] bytes)
        {
            FramedMessageData fmdata;

            fmdata.sequence = 0;
            fmdata.type = 0;

            if (bytes == null)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: bytes = null!!");
            }

            MemoryStream ms = new MemoryStream(bytes);

            using (BinaryReader reader = new BinaryReader(ms))
            {
                try
                {
                    fmdata.type = (FramedMessageType)reader.ReadByte();
                    fmdata.sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: " + ex.Message);
                }
            }
            return fmdata;
        }

        public FramedMessageAck FMA_FromBytes(byte[] bytes)
        {
            FramedMessageAck fmack;

            fmack.type = 0;
            fmack.ack_sequence = 0;
            fmack.received = 0;

            if (bytes == null)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: bytes = null!!");
            }

            MemoryStream ms = new MemoryStream(bytes);

            using (BinaryReader reader = new BinaryReader(ms))
            {
                try
                {
                    fmack.type = (FramedMessageType)reader.ReadByte();
                    fmack.ack_sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
                    fmack.ack_sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: " + ex.Message);
                }
            }
            return fmack;
        }

        public byte[] ToBytes(FramedMessageData fmdata, byte[] message)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write((Byte)fmdata.type);
                writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)fmdata.sequence));

                byte[] len_message = new byte[] {         /* 3 byte message length network ordered... sick */
                    (byte)((((UInt32)message.Length) >> 16) & 0xFF),
                    (byte)((((UInt32)message.Length) >> 8) & 0xFF),
                    (byte)((((UInt32)message.Length) >> 0) & 0xFF)
                };

                writer.Write(len_message);
                writer.Write(message);
            }
            return ms.ToArray();
        }

        public byte[] ToBytes(FramedMessageAck fmack)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write((Byte)fmack.type);
                writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)fmack.ack_sequence));
                writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)fmack.received));
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Add frame header to outgoing user message
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="reload_connection">The reload_connection.</param>
        /// <returns></returns>
        private byte[] addFrameHeader(ReloadConnectionTableEntry connectionTableEntry, byte[] message)
        {
            if (ReloadGlobals.Framing)
            {
                /* Add FH, manage connections */
                FramedMessageData fh_message_data = new FramedMessageData();
                fh_message_data.type = FramedMessageType.data;
                fh_message_data.sequence = connectionTableEntry.fh_sequence;

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("Tx FH DATA {0}", connectionTableEntry.fh_sequence));

                connectionTableEntry.fh_sent.Add(connectionTableEntry.fh_sequence++, DateTime.Now);
                return ToBytes(fh_message_data, message);
            }
            else
            {
                return message;
            }
        }

        /// <summary>
        /// Analyse the frame header, calculate RTO.
        /// </summary>
        /// <param name="fh_message">The fh_message.</param>
        /// <param name="reload_connection">The reload_connection.</param>
        /// <returns></returns>
        private byte[] analyseFrameHeader(ReloadConnectionTableEntry connectionTableEntry, byte[] fh_message, ref uint read_bytes)
        {
            if (ReloadGlobals.Framing)
            {
                /* Handle FrameHeader */
                FramedMessageType type = (FramedMessageType)fh_message[0];
                if (type == FramedMessageType.ack)
                {
                    FramedMessageAck fh_ack = FMA_FromBytes(fh_message);
                    read_bytes = 9;

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("Rx FH ACK {0}, 0x{1:x}", fh_ack.ack_sequence, fh_ack.received));

                    /* Calculate RTO */
                    DateTime sent;
                    if (connectionTableEntry.fh_sent.TryGetValue(fh_ack.ack_sequence, out sent))
                    {
                        long rtt = DateTime.Now.Ticks - sent.Ticks;
                        if (connectionTableEntry.srtt == 0.0)
                        {
                            connectionTableEntry.srtt = rtt;
                            connectionTableEntry.rttvar = 0.5 * rtt;
                            connectionTableEntry.rto = new TimeSpan(Convert.ToInt64(rtt + 4 * connectionTableEntry.rttvar));
                        }
                        else
                        {
                            double alpha = 0.125;
                            double beta = 0.25;
                            connectionTableEntry.srtt = (1.0 - alpha) * connectionTableEntry.srtt + alpha * rtt;
                            connectionTableEntry.rttvar = (1.0 - beta) * connectionTableEntry.rttvar + beta * System.Math.Abs(connectionTableEntry.srtt - rtt);
                            connectionTableEntry.rto = new TimeSpan(Convert.ToInt64(connectionTableEntry.srtt + 4 * connectionTableEntry.rttvar));
                        }
                        connectionTableEntry.fh_sent.Remove(fh_ack.ack_sequence);
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("RTT {0}, SRTT {1}, RTTVAR {2}, RTO {3}", rtt, connectionTableEntry.srtt, connectionTableEntry.rttvar, connectionTableEntry.rto));
                    }
                }
                else
                {
                    if (type == FramedMessageType.data)
                    {
                        FramedMessageData fh_data = FMD_FromBytes(fh_message);
                        read_bytes = 8; //TODO: why 8???
                        byte[] fh_stripped_data = new byte[fh_message.Length - 8];
                        Array.Copy(fh_message, 8, fh_stripped_data, 0, fh_message.Length - 8);

                        UInt32 received = 0;
                        UInt32 n = fh_data.sequence;
                        /* Calculate FH received mask */
                        foreach (UInt32 m in connectionTableEntry.fh_received)
                        {
                            if (m < n || m >= (n - 32))
                            {
                                UInt32 bit = n - m - 1;
                                if (bit < 32)
                                    received |= ((UInt32)1 << (int)bit);
                            }
                        }
                        while (connectionTableEntry.fh_received.Count >= 32)
                            connectionTableEntry.fh_received.Dequeue();
                        connectionTableEntry.fh_received.Enqueue(fh_data.sequence);

                        /* Acknowledge it */
                        FramedMessageAck fh_ack = new FramedMessageAck();
                        fh_ack.type = FramedMessageType.ack;
                        fh_ack.ack_sequence = fh_data.sequence;
                        fh_ack.received = received;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("Tx FH ACK {0}, 0x{1:x}", fh_ack.ack_sequence, fh_ack.received));

                        //in - offset out - bytesprocessed

                        ReloadSendParameters send_params = new ReloadSendParameters()
                        {
                            connectionTableEntry = connectionTableEntry,
                            destinationAddress = null,
                            port = 0,
                            buffer = ToBytes(fh_ack),
                            frame = false,
                            done = new Port<bool>(),
                        };
                        Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(null, send_params, Send));

                        return fh_stripped_data;
                    }
                    else
                      {
                          // unknown type try next byte
                          read_bytes = 1;
                          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,"UNKNOWN TYPE="+type);
                      }
                    
                  }
                
                return null;
            }
            else
            {

                read_bytes = 0;
                return fh_message;

            }
        }
    }
}
