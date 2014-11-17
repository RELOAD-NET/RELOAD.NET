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
using System.Collections.Concurrent;
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

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using TSystems.RELOAD;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Utils;

using System.Threading;
using System.Threading.Tasks;


namespace TSystems.RELOAD.ForwardAndLinkManagement
{

    internal class OverlayLinkTLS
    {
        private byte[] receive_buffer = null; // only used without ssl and fragmentation 12000= max ssl segment

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

        /* Prevent concurrent writing access */
        //ConcurrentQueue<byte[]> writePendingData = new ConcurrentQueue<byte[]>();
        //bool sendingData = false;


        #region Tasks

#if WINDOWS_PHONE
		// Listening sockets is not supported
		private IEnumerator<ITask> linkListen(int port)
		{
			return null;
		}
#else
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

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("TLS_S: Waiting for connections on port {0}", port));

            ListenSocket.Listen(1024);

            m_ListenerSocket = ListenSocket;

            while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown)
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

                // code encapsulated in method for easy reuse in ICE processing
                ReloadTLSServer reloadserver;
                InitReloadTLSServer(associatedSocket, false, out reloadserver);
            }
        }

        // callback methods invoked by the RemoteCertificateValidationDelegate
        //
        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None || (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors && ReloadGlobals.SelfSignPermitted)) 
                return true;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Certificate error: {0}", sslPolicyErrors));

            // Do not allow this client to communicate with unauthenticated servers. 
            return false;
        }

        public bool ValidateClientCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None || (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors && ReloadGlobals.SelfSignPermitted))
                return true;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Certificate error: {0}", sslPolicyErrors));

            // Do not allow this server to communicate with unauthenticated clients. 
            return false;
        }

        public void InitReloadTLSServer(Socket associatedSocket, bool isForAppAttach, out ReloadTLSServer reloadserver)
        {
            ReloadTLSServer reload_server = new ReloadTLSServer(associatedSocket);

            reload_server.AssociatedClient = new TcpClient();
            reload_server.AssociatedClient.Client = reload_server.AssociatedSocket;

            /* Setup new SSL Stream */
            SslStream ReceiverSslStream = null;

            // A client has connected
            try
            {
                //ReceiverSslStream = new SslStream(reload_server.AssociatedClient.GetStream(), false);
                ReceiverSslStream = new SslStream(reload_server.AssociatedClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateClientCertificate), null); // use RemoteCertificateValidationCallback
            
                // Debug
                //File.WriteAllBytes("MyCertificate_Server_Debug.cer", m_ReloadConfig.MyCertificate.Export(X509ContentType.Cert));

                reload_server.AssociatedSslStream = ReceiverSslStream;

                // Authenticate the server and require the client to authenticate also

                //reload_server.AssociatedSslStream.BeginAuthenticateAsServer(
                //    m_ReloadConfig.MyNetCertificate,    // Client Certificate
                //    true,                               // Require Certificate from connecting Peer
                //    SslProtocols.Tls,                   // Use TLS 1.0
                //    false,                              // check Certificate revokation
                //    iarPort.Post,                       // Callback handler
                //    null                                // Object passed to Callback handler
                //);
                if (reload_server.AssociatedSocket.Connected)
                {
                    try
                    {
                        reload_server.AssociatedSslStream.AuthenticateAsServer(
                            m_ReloadConfig.MyCertificate,    // Client Certificate
                            true,                               // Require Certificate from connecting Peer
                            SslProtocols.Tls,                   // Use TLS 1.0
                            false                               // check Certificate revocation
                        );
                    } 
                    catch(System.Security.Authentication.AuthenticationException authex)
                    {

                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, ex.Message);
                    }

                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("TLS_S: TLS Connection established: Protocol: {0}, Key exchange: {1} strength {2}, Hash: {3} strength {4}, Cipher: {5} strength {6}, IsEncrypted: {7}, IsSigned: {8}",
                      reload_server.AssociatedSslStream.SslProtocol,
                      reload_server.AssociatedSslStream.KeyExchangeAlgorithm, reload_server.AssociatedSslStream.KeyExchangeStrength,
                      reload_server.AssociatedSslStream.HashAlgorithm, reload_server.AssociatedSslStream.HashStrength,
                      reload_server.AssociatedSslStream.CipherAlgorithm, reload_server.AssociatedSslStream.CipherStrength,
                      reload_server.AssociatedSslStream.IsEncrypted, reload_server.AssociatedSslStream.IsSigned));

                    X509Certificate2 remoteCert = new X509Certificate2(reload_server.AssociatedSslStream.RemoteCertificate);
                    CertificateValidate(reload_server, remoteCert, isForAppAttach);
                }

            }
            catch (AuthenticationException ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Exception: {0}", ex.Message));

                if (ex.InnerException != null)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Inner exception: {0}", ex.InnerException.Message));
                }
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Authentication failed - closing the connection."));
                reload_server.AssociatedSslStream.Close();
                reload_server.AssociatedClient.Close();
            }
            catch (Exception ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Receiving data failed: {0}", ex.Message));
            }

            // App Attach --arc
            //
            if (isForAppAttach) // Leave here if TLS Server is for app attach
            {
                reloadserver = reload_server; // out param
                return;
            }
            else
                reloadserver = null;

            if (associatedSocket != null)
            {
                OnOpenConnection(reload_server);

                //yield return Arbiter.Receive(false, iarPort, iar =>
                //{
                //    try
                //    {
                //        reload_server.AssociatedSslStream.EndAuthenticateAsServer(iar);
                //    }
                //    catch (Exception ex)
                //    {
                //        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Ending the Authentification (Server Side) failed: {0}", ex.Message));
                //    }
                //});

                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<object>(reload_server, linkReceive));

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_S: {0}, Accepted client {1}", ReceiverSslStream.GetHashCode(), associatedSocket.RemoteEndPoint));
            }
        }
#endif

        /// <summary>
        /// TASK: Socket data reception for server and client part.
        /// </summary>
        /// <param name="secure_object">The secure_object.</param>
        /// <returns></returns>
        private IEnumerator<ITask> linkReceive(object secure_object)
        {
            IAssociation association = (IAssociation)secure_object;
            while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
            {

                // Port<IAsyncResult>: It enqueues messages and keeps track of receivers that can consume messages.
                // IAsyncResult: Type for messages that can be enqueued - Represents the status of an asynchronous operation.
                var iarPort = new Port<IAsyncResult>();
                int bytesReceived = 0;
                NetworkStream ns = null;

                // try to get NetworkStream
                if (association.AssociatedClient.Connected)
                {
                    try
                    {
                        ns = association.AssociatedClient.GetStream();
                    }
                    catch (Exception ex)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Get NetworkStream: " + ex.Message);
                    }
                }

                // Asynchronously receive data from socket
                // BeginReceive(StorageLocation, PositionInStorageLocation, NumberBytesToReceive, Flags, AsyncCallbackDelegate, State);
                // Callback: iarPort.Post = Enqueues a message instance (this?)


                // first we have to read the RFC 4571 framing header (16 bit) out of NetworkStream
                byte[] rfc4571Header = new byte[2];

                try
                {
                    if (ns == null)
                    {
                    }

                    ns.BeginRead(
                        rfc4571Header,
                        0,
                        2,
                        iarPort.Post,
                        null);
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Error reading RFC 4571 framing header: " + ex.Message);
                }


                yield return Arbiter.Receive(false, iarPort, iar =>
                {
                    try
                    {
                        if (association.AssociatedClient.Connected) // TEST
                            bytesReceived = association.AssociatedClient.GetStream().EndRead(iar);

                    }
                    catch
                    {
                        bytesReceived = 0;
                    }
                });


                // convert RFC 4571 byte header to uint16 
                UInt16 rfc4571Header_uint16 = NetworkByteArray.ReadUInt16(rfc4571Header, 0);

                // try to get the message
                try
                {
                    // Reload message
                    if (rfc4571Header_uint16 != 0)
                    {
                        association.AssociatedSslStream.BeginRead(
                        association.InputBuffer,
                        association.InputBufferOffset,
                        association.InputBuffer.Length - association.InputBufferOffset,
                        iarPort.Post,
                        null);
                    }

                    // STUN message
                    else if (rfc4571Header_uint16 == 0)
                    {
                        // TODO: lese STUN Msg von NetworkStream
                    }


                }
                catch (Exception ex)
                {
                    if (ex is SocketException)
                    {
                        HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
                    }
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send: " + ex.Message);

                }

                // Creates a single item receiver
                // Execute handler on message arrival
                yield return Arbiter.Receive(false, iarPort, iar =>
                {
                    try
                    {
                        // Reload message
                        if (rfc4571Header_uint16 != 0)
                        {
                            bytesReceived = association.AssociatedSslStream.EndRead(iar);
                        }

                        // STUN message
                        else if (rfc4571Header_uint16 == 0)
                        {
                            bytesReceived = association.AssociatedClient.GetStream().EndRead(iar);
                        }


                    }
                    catch
                    {
                        bytesReceived = 0;
                    }
                });


                if (bytesReceived <= 0)
                {
                    // Close Socket if exception was thrown or stream is closed
                    //HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
                    //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("linkReceive: {0}, connection closed", association.AssociatedSslStream.GetHashCode()));
                    //break;

                    Thread.Yield();

                }

                association.InputBufferOffset += bytesReceived;
                while (association.InputBufferOffset > 0)
                {

                    int len = Math.Min(association.InputBuffer.Length, association.InputBufferOffset);
                    association.InputBufferOffset -= len;

                    m_ReloadConfig.Statistics.BytesRx = (ulong)len;

                    if (len == 0)
                    {
                        association.InputBufferOffset = 0;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SBB_OnReceive: Clearing association InputBuffer on MaxSize=0");
                    }

                    if (association.InputBufferOffset > 0)
                        Buffer.BlockCopy(association.InputBuffer, len, association.InputBuffer, 0, association.InputBufferOffset);

                    // Forward Data to SBB to decrypt
                    if (association.AssociatedSocket.Connected)

                        OnData(association);

                    else
                    {
                        HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
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
        private IEnumerator<ITask> linkSend(Node node, ReloadSendParameters send_params)    // node is remote peer
        {
            // connectionTableEntry is only null if NO ICE is used, or the remote node is a bootstrap ( see TLS.FLM.Send() ) 
            if (send_params.connectionTableEntry == null)
            {
                ////if (ReloadGlobals.UseNoIce || node.Id == null)


                //// if remote peer is a bootstrap, we don't need ICE procedures
                //if (ReloadGlobals.UseNoIce || remoteIsBS)
                //{

                /* No open connection, open new connection */
                Socket socket = new Socket(send_params.destinationAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

#if !WINDOWS_PHONE
                // Socket.Handle is not supported by WP7
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: Connect socket {0}, {1}:{2}", socket.Handle, send_params.destinationAddress, send_params.port));
#endif

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

                // code encapsulated in method for easy reuse in ICE processing
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("linkSend: Authenticating as Client on {0}", socket.LocalEndPoint));

                //// TODO: change InitReloadTLSClient and StartReloadTLSClient IPEndpoint  attacherEndpoint param to String attacher (== Subject from attacher certificate)
                ////       a fetch for the CertificateStore usage can give you the Subject here.
                IPEndPoint attacherEndpoint = new IPEndPoint(send_params.destinationAddress, send_params.port); 
                ReloadTLSClient reloadclient;
                InitReloadTLSClient(send_params, socket, attacherEndpoint, false, out reloadclient);
                                
            }

            send_params.done.Post(true); // connection attempt for send_params is finished --joscha

            if (send_params.frame)
                send_params.buffer = addFrameHeader(send_params.connectionTableEntry, send_params.buffer);

            IAssociation association = ((IAssociation)send_params.connectionTableEntry.secureObject);
            if (association.TLSConnectionIsOpen)
            {
                Send(association, send_params.buffer);
                send_params.connectionTableEntry.LastActivity = DateTime.Now;    /* Re-trigger activity timer */
            }
            else
                association.TLSConnectionWaitQueue.Enqueue(send_params.buffer);
        }

        private IEnumerator<ITask> ICElinkSend(Node node, ReloadSendParameters send_params)    // node is remote peer
        {
            // connectionTableEntry is only null if NO ICE is used, or the remote node is a bootstrap ( see TLS.FLM.Send() ) 
            if (send_params.connectionTableEntry == null)
            {               

//                /* No open connection, open new connection */
//                Socket socket = new Socket(send_params.destinationAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

//#if !WINDOWS_PHONE
//                // Socket.Handle is not supported by WP7
//                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: Connect socket {0}, {1}:{2}", socket.Handle, send_params.destinationAddress, send_params.port));
//#endif

//                var iarPort = new Port<IAsyncResult>();
//                socket.BeginConnect(new IPEndPoint(send_params.destinationAddress, send_params.port), iarPort.Post, null);

//                bool connectError = false;
//                yield return Arbiter.Receive(false, iarPort, iar =>
//                {
//                    try
//                    {
//                        socket.EndConnect(iar);
//                    }
//                    catch (Exception ex)
//                    {
//                        HandleRemoteClosing(node);
//                        connectError = true;
//                        m_ReloadConfig.Statistics.IncConnectionError();
//                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, ex.Message);
//                        if (ReloadFLMEventHandler != null)
//                            ReloadFLMEventHandler(this, new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED, null, null));

//                    }
//                });
//                if (connectError)
//                {
//                    send_params.done.Post(true);    //--joscha
//                    yield break;
//                }


                if (send_params.connectionSocket != null && send_params.connectionSocket.Connected)
                {
                    // code encapsulated in method for easy reuse in ICE processing
                    ReloadTLSClient reloadclient;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("ICElinkSend: Authenticating as Client on {0}", send_params.connectionSocket.LocalEndPoint));
                    InitReloadTLSClient(send_params, send_params.connectionSocket, new IPEndPoint(IPAddress.Any, 0), false, out reloadclient /*ICElinkSend is never used, so the attacherEndpoint and isForAppAttach param does not matter*/);
                }
                else
                {
                    
                    HandleRemoteClosing(node);
                    //connectError = true;
                    m_ReloadConfig.Statistics.IncConnectionError();
                    //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, ex.Message);
                    if (ReloadFLMEventHandler != null)
                        ReloadFLMEventHandler(this, new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED, null, null));
                    send_params.done.Post(true);    //--joscha
                    yield break;
                }

                                
            }

            send_params.done.Post(true); // connection attempt for send_params is finished --joscha

            if (send_params.frame)
                send_params.buffer = addFrameHeader(send_params.connectionTableEntry, send_params.buffer);

            IAssociation association = ((IAssociation)send_params.connectionTableEntry.secureObject);
            if (association.TLSConnectionIsOpen)
            {
                Send(association, send_params.buffer);
                send_params.connectionTableEntry.LastActivity = DateTime.Now;    /* Re-trigger activity timer */
            }
            else
                association.TLSConnectionWaitQueue.Enqueue(send_params.buffer);
        }

        public void InitReloadTLSClient(ReloadSendParameters send_params, Socket socket, IPEndPoint attacherEndpoint, bool isForAppAttach, out ReloadTLSClient reloadclient)
        {
            ReloadTLSClient reload_client = new ReloadTLSClient(socket);
            reload_client.AssociatedClient = new TcpClient();
            reload_client.AssociatedClient.Client = reload_client.AssociatedSocket;

            /* Setup new SSL Stream */

            //SslStream SenderSslStream = new SslStream(reload_client.AssociatedClient.GetStream(), false);
            SslStream SenderSslStream = new SslStream(reload_client.AssociatedClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null); // use RemoteCertificateValidationCallback

            reload_client.AssociatedSslStream = SenderSslStream;

            reload_client.TLSConnectionOpen = new Port<bool>();

            // Client Authentification
            try
            {
                X509Certificate2Collection certificates = new X509Certificate2Collection();
                certificates.Add(m_ReloadConfig.MyCertificate);

                if (m_ReloadConfig.RootCertificate != null) // root cert is null when using self signed certs
                    certificates.Add(m_ReloadConfig.RootCertificate);


                // Debug
                //File.WriteAllBytes("MyCertificate_Client_Debug.cer", m_ReloadConfig.MyCertificate.Export(X509ContentType.Cert));
                //File.WriteAllBytes("RootCertificate_Client_Debug.cer", m_ReloadConfig.RootCertificate.Export(X509ContentType.Cert));

                String remoteClient = "reload:" + attacherEndpoint.Address.ToString() + ":" + attacherEndpoint.Port.ToString(); //// TODO: change attacherEndpoint param to "attacher" that is equal to subject from attachers certificate (then you don't have to build it here and don't need any requirements on subject names!!!)

                //String remoteClient = TSystems.RELOAD.Enroll.EnrollmentSettings.Default.CN;
                
                // The server name must match the name on the server certificate. 
                reload_client.AssociatedSslStream.AuthenticateAsClient(remoteClient, certificates, SslProtocols.Tls, false);

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("TLS_C: TLS Connection established: Protocol: {0}, Key exchange: {1} strength {2}, Hash: {3} strength {4}, Cipher: {5} strength {6}, IsEncrypted: {7}, IsSigned: {8}",
                    reload_client.AssociatedSslStream.SslProtocol,
                    reload_client.AssociatedSslStream.KeyExchangeAlgorithm, reload_client.AssociatedSslStream.KeyExchangeStrength,
                    reload_client.AssociatedSslStream.HashAlgorithm, reload_client.AssociatedSslStream.HashStrength,
                    reload_client.AssociatedSslStream.CipherAlgorithm, reload_client.AssociatedSslStream.CipherStrength,
                    reload_client.AssociatedSslStream.IsEncrypted, reload_client.AssociatedSslStream.IsSigned));

                X509Certificate2 remoteCert = new X509Certificate2(reload_client.AssociatedSslStream.RemoteCertificate);
                CertificateValidate(reload_client, remoteCert, isForAppAttach);

            }
            catch (AuthenticationException ex)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Exception: {0}", ex.Message));

                if (ex.InnerException != null)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Inner exception: {0}", ex.InnerException.Message));
                }
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Authentication failed - closing the connection."));
                reload_client.AssociatedSslStream.Close();
                reload_client.AssociatedClient.Close();
            }

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: SBB client object {0}", reload_client.GetHashCode()));

            // App Attach --arc
            //
            if (isForAppAttach) // Leave here if TLS Client is for app attach
            {
                reloadclient = reload_client; // out param
                return;
            }
            else 
                reloadclient = null;

            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<object>(reload_client, linkReceive));

            OnOpenConnection(reload_client);

            //if (!ReloadGlobals.TLS_PASSTHROUGH) --joscha
            //{
            //    bool isOpen = false;
            //    yield return Arbiter.Receive(false, reload_client.TLSConnectionOpen, result => { isOpen = result; });

            //    if (!isOpen)
            //    {
            //        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_C: Error Timeout"));
            //        if (ReloadFLMEventHandler != null)
            //            ReloadFLMEventHandler(this, new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED, null, null));
            //        send_params.done.Post(true); // --joscha
            //        yield break;
            //    }
            //}

            /* Add/update connection list */
            send_params.connectionTableEntry = m_connection_table.updateEntry(reload_client);

            if (ReloadGlobals.FRAGMENTATION == true)
            {
                reload_client.AssociatedSocket.NoDelay = true; //--joscha no nagle
            }
        }

        private void HandleRemoteClosing(Node node)
        {
            if (node != null && node.Id != null)
                m_ReloadConfig.ThisMachine.Transport.InboundClose(node.Id);
        }

        private void HandleRemoteClosing(SslStream stream, TcpClient client)
        {
            lock (m_connection_table)
            {
                foreach (KeyValuePair<String, ReloadConnectionTableEntry> entry in m_connection_table)
                {
                    IAssociation association = (IAssociation)entry.Value.secureObject;
                    if (association.AssociatedSslStream == stream)
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_C: lost connection to {0} cleaning connection table", entry));

                        m_connection_table.Remove(entry.Key);
                        m_ReloadConfig.ThisMachine.Transport.InboundClose(entry.Value.NodeID);

                        stream.Close();
                        client.Close();

                        return;
                    }
                }
            }
        }
        #endregion

        private void OnOpenConnection(object Sender)
        {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TLS, String.Format("TLS_{0}: TLS connection opened", Sender is ReloadTLSServer ? "S" : "C"));

            IAssociation association = ((IAssociation)Sender);
            association.TLSConnectionIsOpen = true;

            if (association.TLSConnectionOpen != null)
            {
                association.TLSConnectionOpen.Post(true);
            }

            foreach (object o in association.TLSConnectionWaitQueue)
            {
                byte[] b = (byte[])o;
                Send(association, b);
            }
        }

        /// <summary>
        /// process the received data
        /// </summary>
        /// <param name="Sender">The sender.</param>
        private void
            OnData(object Sender)
        {
            /*            ++ReloadGlobals.DbgPacketCount;
                        File.WriteAllBytes(@"C:\Temp\RELOAD\data0001.dmpa" + ReloadGlobals.DbgPacketCount.ToString("0000") + ".dmp", Buffer);
            
                        //TKTEST
                        if (Buffer.Length > 500)
                          Buffer = File.ReadAllBytes(@"C:\Temp\RELOAD\data0001.dmp");
               */
            IAssociation association = (IAssociation)Sender;
            byte[] Buffer = null;

            if (association.InputBuffer[0] == 128) // Data Frame
            {
                byte[] len_message = new byte[4];
                len_message[0] = association.InputBuffer[7];
                len_message[1] = association.InputBuffer[6];
                len_message[2] = association.InputBuffer[5];
                len_message[3] = 0;

                // + 8 for header fields
                UInt32 length = BitConverter.ToUInt32(len_message, 0) + 8;

                Buffer = new byte[length];
                Array.Copy(association.InputBuffer, Buffer, length);
            }
            if (association.InputBuffer[0] == 129) // Ack Frame
            {
                Buffer = new byte[9]; // sizeof(FramedMessageAck)
                Array.Copy(association.InputBuffer, Buffer, 9);
            }

            //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Received {0} Bytes", Buffer.Length));

            try
            {
                byte[] temp_buf = null;
                if (receive_buffer != null)
                {
                    temp_buf = new byte[Buffer.Length + receive_buffer.Length];

                    Array.Copy(receive_buffer, 0, temp_buf, 0, receive_buffer.Length);
                    Array.Copy(Buffer, 0, temp_buf, receive_buffer.Length, Buffer.Length);
                    Buffer = temp_buf;
                    receive_buffer = null;
                }

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format("SBB_OnData Buffer.Length={0}", Buffer.Length));
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("TLS_{0}: Data received, len {1}", Sender is ReloadTLSServer ? "S" : "C", Buffer.Length));
                //IAssociation association = (IAssociation)Sender; -- moved to top
                ReloadConnectionTableEntry connectionTableEntry = m_connection_table.updateEntry(Sender);
                if (ReloadFLMEventHandler != null)
                {
                    if (connectionTableEntry != null)
                    {
                        ReloadMessage reloadMsg = null;
                        byte[] AnalysedBuffer = null;
                        long bytesProcessed = 0;
                        BinaryReader reader = new BinaryReader(new MemoryStream(Buffer));
                        do
                        { //TODO: optimize! Problem: streaming socket => no guarantee to receive only a single ReloadMessage in one SBB_OnData call also reception of partial messages possible (not handled so far)
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format("SBB_OnData Buffer.Length={0} bytesProcessed={1}", Buffer.Length, bytesProcessed));
                            Byte[] buf = new Byte[Buffer.Length - bytesProcessed];
                            reader.BaseStream.Seek(bytesProcessed, SeekOrigin.Begin);
                            reader.Read(buf, 0, (int)(Buffer.Length - bytesProcessed));
                            uint bytecount = 0;
                            bool isAck = false;
                            AnalysedBuffer = analyseFrameHeader(connectionTableEntry, buf, ref bytecount, ref isAck);

                            bytesProcessed += bytecount;    //framing header
                            if (isAck == true)
                            {

                            }

                            else if (AnalysedBuffer != null)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format("SBB_OnData buf.Length={0} AnalysedBuffer.Length={1} bytesProcessed={2}", buf.Length, AnalysedBuffer.Length, bytesProcessed));
                                //bytesProcessed = 0;
                                long temp = bytesProcessed;
                                reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(AnalysedBuffer, ref temp, ReloadMessage.ReadFlags.full);
                                if (reloadMsg.reload_message_body == null)
                                {  //not all bytes of an fragmented message are received yet

                                    receive_buffer = new byte[buf.Length];
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("reloadMsg.reload_message_body = NULL receive_buffer.length=" + receive_buffer.Length));
                                    Array.Copy(buf, 0, receive_buffer, 0, buf.Length);
                                    return;
                                }
                                //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("MessageLength={0}", temp));
                                bytesProcessed += temp;
                                reloadMsg.LastHopNodeId = connectionTableEntry.NodeID;

                                ReloadFLMEventHandler(this,
                                    new ReloadFLMEventArgs(ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK, connectionTableEntry, reloadMsg));
                            }
                            //// message was invalid but a least the size could be extracted, try to skip to next packet
                            //else if (thisMsgBytes != 0)
                            //{
                            //  bytesProcessed += thisMsgBytes;
                            //  association.InputBufferOffset -= (int)thisMsgBytes; /* Help rx to terminate */
                            //}

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
        /// Callback on request to send TLS data.
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="Buffer">The buffer.</param>
        private void Send(object Sender, byte[] Buffer)
        {

            IAssociation association = (IAssociation)Sender;

            EnqueueDataForWrite(association, Buffer); //threadsafe
        }

        /// <summary>
        /// callback for certificate validation.
        /// </summary>
        /// <param name="Sender">The sender.</param>
        /// <param name="X509Certificate">The X509 certificate.</param>
        /// <param name="Validate">if set to <c>true</c> [validate].</param>
#if WINDOWS_PHONE
		// Because of different callback signatures...
		private void SBB_OnCertificateValidate(object Sender, TElX509Certificate X509Certificate, ref TSBBoolean Validate)
#else
        private void CertificateValidate(object Sender, System.Security.Cryptography.X509Certificates.X509Certificate2 X509Certificate, bool isForAppAttach)
#endif
        {
            string rfc822Name = null;

            NodeId nodeid = ReloadGlobals.retrieveNodeIDfromCertificate(X509Certificate, ref rfc822Name);

            bool Validate = Utils.X509Utils.VerifyCertificate(X509Certificate, m_ReloadConfig.RootCertificate);

            if (!Validate)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TLS_{0}: NodeID {1} Endpoint {2}, Certificate validation failed (CA Issuer {3})", Sender is ReloadTLSServer ? "S" : "C", nodeid, (Sender as IAssociation).AssociatedSocket.RemoteEndPoint.ToString(), X509Certificate.Subject));
                return;
            }

            (Sender as IAssociation).RemoteNodeId = nodeid;

            ReloadConnectionTableEntry connectionTableEntry;

            // replaced lock more above. Got race conditions because of finger table attaches
            lock (m_connection_table)
            {

                connectionTableEntry = m_connection_table.lookupEntry(nodeid);

                if (connectionTableEntry == null && !isForAppAttach) // !isForAppAttach --> don't add the connection for an app attach to connection table
                {
                    connectionTableEntry = new ReloadConnectionTableEntry() { secureObject = Sender, LastActivity = DateTime.Now };
                    connectionTableEntry.NodeID = nodeid;
                    m_connection_table.Add(nodeid.ToString(), connectionTableEntry);
                }
            }

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TLS, String.Format("TLS_{0}: Got NodeID {1}, set to {2}", Sender is ReloadTLSServer ? "S" : "C", connectionTableEntry.NodeID, (Sender as IAssociation).AssociatedSocket.RemoteEndPoint.ToString()));
        }

        /// <summary>
        /// Socket async send callback.
        /// </summary>
        /// <param name="ar">The ar.</param>
        private void socketAsyncSendCallback(IAsyncResult ar)
        {
            IAssociation association = (IAssociation)ar.AsyncState;
            try
            {
                association.AssociatedSslStream.EndWrite(ar);
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, "AsyncSend SocketError:" + ((SocketException)ex).ErrorCode.ToString());
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AsyncSend: " + ex.Message);
            }

            Write(association);
        }

        /// <summary>
        /// Socket async send callback RFC 4571 Header.
        /// </summary>
        /// <param name="ar">The ar.</param>
        private void socketAsyncSendCallbackRfc4571Header(IAsyncResult ar)
        {
            IAssociation association = (IAssociation)ar.AsyncState;
            try
            {
                //association.AssociatedSslStream.EndWrite(ar);
                association.AssociatedClient.GetStream().EndWrite(ar);
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, "AsyncSend SocketError:" + ((SocketException)ex).ErrorCode.ToString());
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AsyncSend: " + ex.Message);
            }

            //Write(association);
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
            //yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, linkSend)); //original

            //markus
            if (send_params.connectionSocket == null)
                yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, linkSend));

            else
                yield return Arbiter.ExecuteToCompletion(m_DispatcherQueue, new IterativeTask<Node, ReloadSendParameters>(node, send_params, ICElinkSend));

        }
        #endregion

        internal void ShutDown()
        {
            try
            {
                if (m_connection_table != null)
                    foreach (KeyValuePair<string, ReloadConnectionTableEntry> pair in m_connection_table)
                    {
                        IAssociation association = (IAssociation)pair.Value.secureObject;
                        if (association.AssociatedSocket != null)
                        {
                            if (association.AssociatedSocket.Connected)
                            {
                                association.AssociatedSslStream.Close();
                                association.AssociatedClient.Close();
                            }
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

                connectionTableEntry.fh_sent[connectionTableEntry.fh_sequence++] =  DateTime.Now;
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
        private byte[] analyseFrameHeader(ReloadConnectionTableEntry connectionTableEntry, byte[] fh_message, ref uint read_bytes, ref bool ack)
        {
            if (ReloadGlobals.Framing)
            {
                /* Handle FrameHeader */
                FramedMessageType type = (FramedMessageType)fh_message[0];
                if (type == FramedMessageType.ack)
                {
                    ack = true;
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
                        ack = false;
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
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "UNKNOWN TYPE=" + type);
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

        /// <summary>
        /// Enqueue the Data to Write in the ConcurrentQueue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="buffer"></param>
        void EnqueueDataForWrite(IAssociation sender, byte[] buffer)
        {
            if (buffer == null)
                return;

            sender.WritePendingData.Enqueue(buffer);

            lock (sender.WritePendingData)
            {
                if (sender.SendingData)
                    return;
                else
                    sender.SendingData = true;

                Write(sender);
            }
        }

        /// <summary>
        /// Write Data in Queue to SslStream
        /// </summary>
        /// <param name="sender"></param>
        void Write(IAssociation sender)
        {

            byte[] buffer = null;
            NetworkStream ns = null;

            // try to get NetworkStream for framing method (RFC 4571)
            if (sender.AssociatedClient.Connected)
            {
                try
                {
                    ns = sender.AssociatedClient.GetStream();
                }
                catch (Exception ex)
                {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Get NetworkStream: " + ex.Message);
                }
            }


            try
            {
                if (sender.WritePendingData.Count > 0 && sender.WritePendingData.TryDequeue(out buffer))
                {

                    // Reload Msg Data + Reload Framing Header
                    if ((buffer[0] == (byte)128) && ReloadMessage.RELOTAG == NetworkByteArray.ReadUInt32(buffer, 8))
                    {
                        // RFC 4571 Framing method
                        ns.BeginWrite(NetworkByteArray.CreateUInt16((UInt16)buffer.Length), 0, sizeof(UInt16), new AsyncCallback(socketAsyncSendCallbackRfc4571Header), sender);

                        sender.AssociatedSslStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(socketAsyncSendCallback), sender);
                        m_ReloadConfig.Statistics.BytesTx = (ulong)buffer.Length + 2;
                    }

                    // Reload Msg Ack + Reload Framing Header
                    else if (buffer[0] == (byte)129)
                    {
                        // RFC 4571 Framing method
                        ns.BeginWrite(NetworkByteArray.CreateUInt16((UInt16)buffer.Length), 0, sizeof(UInt16), new AsyncCallback(socketAsyncSendCallbackRfc4571Header), sender);

                        sender.AssociatedSslStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(socketAsyncSendCallback), sender);
                        m_ReloadConfig.Statistics.BytesTx = (ulong)buffer.Length + 2;
                    }


                    // Reload Msg without Framing Header
                    else if (ReloadMessage.RELOTAG == NetworkByteArray.ReadUInt32(buffer, 7))
                    {
                        // RFC 4571 Framing method
                        ns.BeginWrite(NetworkByteArray.CreateUInt16((UInt16)buffer.Length), 0, sizeof(UInt16), new AsyncCallback(socketAsyncSendCallbackRfc4571Header), sender);

                        sender.AssociatedSslStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(socketAsyncSendCallback), sender);
                        m_ReloadConfig.Statistics.BytesTx = (ulong)buffer.Length + 2;
                    }

                    // STUN Msg
                    else if (STUN.STUNMessage.m_MagicCookie == NetworkByteArray.ReadUInt32(buffer, 4))
                    {
                        // RFC 4571 Framing method (keine App Daten, daher Länge = 0)
                        ns.BeginWrite(NetworkByteArray.CreateUInt16(0), 0, sizeof(UInt16), new AsyncCallback(socketAsyncSendCallbackRfc4571Header), sender);

                        // TODO: STUN Msg in NetworkStream schreiben

                        m_ReloadConfig.Statistics.BytesTx = (ulong)buffer.Length + 2;
                    }



                }
                else
                {
                    lock (sender.WritePendingData)
                    {
                        sender.SendingData = false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                {
                    HandleRemoteClosing(sender.AssociatedSslStream, sender.AssociatedClient);
                }
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send: " + ex.Message);

                lock (sender.WritePendingData)
                {
                    sender.SendingData = false;
                }
            }
        }

    }
}
