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

#define CONNECTION_MANAGEMENT

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using System.Xml.Serialization;

using Microsoft.Ccr.Core;
using System.Runtime.InteropServices;

using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;

namespace TSystems.RELOAD.ForwardAndLinkManagement {
  /// <summary>
  /// Simple Link and Forwarding layer 
  /// </summary>
  public sealed class SimpleFLM : IForwardLinkManagement {
    private SimpleOverlayLink link;

    public event ReloadFLMEvent ReloadFLMEventHandler;
    private Statistics m_statistics;

    private ReloadOverlayConfiguration m_ReloadOverlayConfiguration = null;

    public ReloadOverlayConfiguration ReloadOverlayConfiguration {
      get { return m_ReloadOverlayConfiguration; }
    }

    NodeId m_node_id = null;
    public NodeId LocalNodeID {
      get { return m_node_id; }
    }

    ReloadConfig m_ReloadConfig = null;

    /// <summary>
    /// Provides connection table info.
    /// </summary>
    /// <returns></returns>
    public List<ReloadConnectionTableInfoElement> ConnectionTable {
      get {
        List<ReloadConnectionTableInfoElement> result = new List<ReloadConnectionTableInfoElement>();

        lock (link.ConnectionTable) {
          foreach (string key in link.ConnectionTable.Keys) {
            ReloadConnectionTableInfoElement reload_connection_info = new ReloadConnectionTableInfoElement();
            SimpleOverlayConnectionTableElement connectionTableEntry = link.ConnectionTable[key];
            reload_connection_info.AssociatedSocket = connectionTableEntry.AssociatedSocket;
            reload_connection_info.RemainingUpTime = connectionTableEntry.RemainingUpTime;
            reload_connection_info.NodeID = connectionTableEntry.NodeID;
            reload_connection_info.LastActivity = connectionTableEntry.LastActivity;
            reload_connection_info.Outbound = connectionTableEntry.Outbound;
            result.Add(reload_connection_info);
          }
        }
        return result;
      }
    }

    ReloadFLMEventArgs link_ReloadFLMEventHandler(object sender, ReloadFLMEventArgs args) {
      if (ReloadFLMEventHandler == null)
        throw new System.Exception("SimpleFLM: No ReloadFLMEventHandler installed");

      switch (args.Eventtype) {
        case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK:
          if (args.Message != null &&
              ReloadFLMEventHandler != null)
            ReloadFLMEventHandler(sender, args);
          break;
        case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_FRAME_SEND_BUFFER:
          /* Internal message */
          break;
        case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED:
          if (ReloadFLMEventHandler != null)
            ReloadFLMEventHandler(sender, args);
          break;
      }
      return args;
    }

    public bool Init() {
      NodeId nullNode = new NodeId(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
      if (m_ReloadConfig.LocalNodeID == nullNode || m_ReloadConfig.LocalNodeID == null)
        m_ReloadConfig.LocalNodeID = ReloadGlobals.GetHash(
          System.Text.Encoding.ASCII.GetBytes(
          ReloadGlobals.HostName.ToUpper() + m_ReloadConfig.ListenPort.ToString()));

      m_node_id = m_ReloadConfig.LocalNodeID;

      if (m_ReloadConfig.DispatcherQueue != null) {
        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<int>(m_ReloadConfig.ListenPort, link.Listen));
        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask(link.ManageConnections));
      }
      return true;
    }

    public SimpleFLM(ReloadConfig reloadConfig) {
      m_ReloadConfig = reloadConfig;
      m_statistics = reloadConfig.Statistics;
      link = new SimpleOverlayLink(reloadConfig);
      link.ReloadFLMEventHandler += new ReloadFLMEvent(link_ReloadFLMEventHandler);
    }

    public IEnumerator<ITask> Send(Node node, ReloadMessage reloadMessage) {
      if (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<Node, byte[]>(node, reloadMessage.ToBytes(), link.Send));
      yield break;
    }
    public bool NextHopInConnectionTable(NodeId next_hop_node_id) {
      return false;
    }

    /// <summary>
    /// Shut downs listeners and receivers.
    /// </summary>
    /// <returns></returns>
    public void ShutDown() {
      link.ShutDown();
    }
  }

  public class SimpleOverlayConnectionTableElement {
    public Socket AssociatedSocket;
    public NodeId NodeID;
    public TimeSpan RemainingUpTime;
    public DateTime Start;
    public DateTime LastActivity;
    //store type of connection establishment here
    public bool Outbound = true;
    //related to framing
    internal UInt32 fh_sequence;
    internal Queue<UInt32> fh_received = new Queue<UInt32>(32);
    internal Dictionary<UInt32, DateTime> fh_sent = new Dictionary<uint, DateTime>(32);
    internal object fh_sent_sync_object = new object();
    internal double srtt = 0.0;
    internal double rttvar;
    internal TimeSpan rto = new TimeSpan(0, 0, 3);    /* RFC 2988 start value */
  }

  public class SimpleOverlayLink {
    public event ReloadFLMEvent ReloadFLMEventHandler;

    private ReloadConfig m_ReloadConfig;

    private Socket m_ListenerSocket = null;

    public Dictionary<string, SimpleOverlayConnectionTableElement> m_connection_table = new Dictionary<string, SimpleOverlayConnectionTableElement>();

    public Dictionary<string, SimpleOverlayConnectionTableElement> ConnectionTable {
      get { return m_connection_table; }
    }

    public SimpleOverlayLink(ReloadConfig reloadConfig) {
      m_ReloadConfig = reloadConfig;
    }

    public IEnumerator<ITask> ManageConnections() {
      while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        if (m_ReloadConfig.State >= ReloadConfig.RELOAD_State.Configured)
          try {
            List<string> list = null;

            if (m_ReloadConfig.AdmittingPeer != null)
              foreach (KeyValuePair<string, SimpleOverlayConnectionTableElement> pair in m_connection_table) {
                //only kill outbound connections, think of clients, who need a connection to admitting peer
                if (pair.Value.Outbound)
                  if ((DateTime.Now - pair.Value.LastActivity).TotalSeconds > ReloadGlobals.CHORD_UPDATE_INTERVAL + 30) {
                    Socket AssociatedSocket = pair.Value.AssociatedSocket;
                    if (AssociatedSocket != null && pair.Value.AssociatedSocket.Connected) {
                      //don't kill connection to admitting peer as client
                      if (!m_ReloadConfig.IamClient || pair.Value.NodeID != m_ReloadConfig.AdmittingPeer.Id) {
                        if (AssociatedSocket.Connected)
                          AssociatedSocket.Shutdown(SocketShutdown.Both);
                        AssociatedSocket.Close();

                        if (list == null)
                          list = new List<string>();

                        //defer deletion to avoid enumeration change issues
                        list.Add(pair.Key);
                      }
                    }
                  }
              }

            lock (m_connection_table) {
              if (list != null)
                foreach (string key in list) {
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, String.Format("SimpleFLM: || {0} Closing connection on inactivity", key));
                  m_connection_table.Remove(key);
                }
            }
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SimpleFLM CleanUpSockets: " + ex.Message);
          }

        Port<DateTime> timeoutPort = new Port<DateTime>();
        m_ReloadConfig.DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0, ReloadGlobals.MAINTENANCE_PERIOD), timeoutPort);
        yield return Arbiter.Receive(false, timeoutPort, x => { });
      }
    }

    public IEnumerator<ITask> Listen(int port) {
      IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);

      Socket ListenSocket = new Socket(endPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
      ListenSocket.Bind(endPoint);
      ListenSocket.Listen(1024);

      m_ListenerSocket = ListenSocket;

      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT,
        "SimpleFLM: Waiting for connections");

      while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        var iarPort = new Port<IAsyncResult>();
        Socket associatedSocket = null;

        ListenSocket.BeginAccept(iarPort.Post, null);
        
        yield return Arbiter.Receive(false, iarPort, iar => {
          try {
            associatedSocket = ListenSocket.EndAccept(iar);
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Link.Listen: " + ex);
          }
        });        

        if (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
            String.Format("SimpleFLM: << {0} accepted client", associatedSocket.RemoteEndPoint));
          Arbiter.Activate(m_ReloadConfig.DispatcherQueue,
            new IterativeTask<Socket, NodeId>(associatedSocket, null, Receive));
        }
      }
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: << Exit from listen"));
    }

    private IEnumerator<ITask> Receive(Socket socketClient, NodeId nodeid) {
      if (socketClient == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("SimpleFLM: Receive: socket == null!!!"));
        yield break;
      }

      if (!socketClient.Connected) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("SimpleFLM: << {0} receive, but client is not connected", socketClient.RemoteEndPoint));
        HandleRemoteClosing(socketClient);
        yield break;
      }

      while (socketClient != null && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
        byte[] buffer = new byte[ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP];

        var iarPort = new Port<IAsyncResult>();
        int bytesReceived = 0;

        try {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: << {0} BeginReceive", socketClient == null ? "null" : socketClient.RemoteEndPoint.ToString()));
          socketClient.BeginReceive(
              buffer,
              0,
              buffer.Length,
              SocketFlags.None, iarPort.Post, null);
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("SimpleFLM: << {0} BeginReceive", socketClient == null ? "null" : socketClient.RemoteEndPoint.ToString()) + ex.Message);
        }
        yield return Arbiter.Receive(false, iarPort, iar => {
          try {
            if (iar != null)
              bytesReceived = socketClient.EndReceive(iar);
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
              String.Format("SimpleFLM: << {0} Receive: {1} ",
              nodeid == null ? "" : nodeid.ToString(), ex.Message));
          }

          if (bytesReceived <= 0) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, 
              String.Format("SimpleFLM: << {0} Receive: lost connection, closing socket",
              socketClient.RemoteEndPoint));
            HandleRemoteClosing(socketClient);
            socketClient.Close();
            socketClient = null;
            return;
          }

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
            String.Format("SimpleFLM: << {0} Read {1} bytes from {2}",
            socketClient.RemoteEndPoint, 
            bytesReceived, nodeid == null ? "" : nodeid.ToString()));

          m_ReloadConfig.Statistics.BytesRx = (UInt64)bytesReceived;

#if CONNECTION_MANAGEMENT
          /* beginn connection management */
          long bytesProcessed = 0;
          SimpleOverlayConnectionTableElement socte = null;

          if (ReloadGlobals.Framing) {
            foreach (KeyValuePair<string, SimpleOverlayConnectionTableElement> pair in m_connection_table) {
              if (socketClient == pair.Value.AssociatedSocket) {
                socte = pair.Value;
                break;
              }
            }

            if (socte == null)
              socte = new SimpleOverlayConnectionTableElement();
            Array.Resize(ref buffer, bytesReceived);
            buffer = analyseFrameHeader(socte, buffer);
            bytesReceived = buffer.Length;
          }

          ReloadMessage reloadMsg = null;

          if (buffer != null) {
            reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(buffer,
              ref bytesProcessed, ReloadMessage.ReadFlags.full);
          }

          if (socketClient != null && reloadMsg != null) {
            if (nodeid == null)
              nodeid = reloadMsg.LastHopNodeId;

            if (nodeid != null)
              if (m_connection_table.ContainsKey(nodeid.ToString())) {
                SimpleOverlayConnectionTableElement rcel = m_connection_table[
                  nodeid.ToString()];
                rcel.LastActivity = DateTime.Now;
              }
              else {
                SimpleOverlayConnectionTableElement rcel = socte;
                if (rcel == null)
                  rcel = new SimpleOverlayConnectionTableElement();
                rcel.NodeID = reloadMsg.LastHopNodeId;
                rcel.AssociatedSocket = socketClient;
                /*
                 * tricky: if this is an answer, this must be issued by an 
                 * outgoing request before (probably the first 
                 * bootstrap contact, where we have no nodeid from)
                 */
                rcel.Outbound = !reloadMsg.IsRequest();
                rcel.LastActivity = rcel.Start = DateTime.Now;

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
                  String.Format("SimpleFLM: << {0} Receive: Associating node {1}",
                  socketClient.RemoteEndPoint, rcel.NodeID.ToString()));
                lock (m_connection_table) {
                  if (nodeid != m_ReloadConfig.LocalNodeID) {
                    if (!m_connection_table.ContainsKey(rcel.NodeID.ToString()))
                      m_connection_table.Add(rcel.NodeID.ToString(), rcel);
                    else
                      m_connection_table[rcel.NodeID.ToString()] = rcel;
                  }
                }
              }
            /* end connection management */

            if (ReloadFLMEventHandler != null) {
              //there might by more then one packet inside
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
                String.Format("SimpleFLM: << {0} <== {1} {2}, TransID={3:x16}", socketClient.RemoteEndPoint, reloadMsg.reload_message_body.RELOAD_MsgCode.ToString(), nodeid.ToString(), reloadMsg.TransactionID));


              ReloadFLMEventHandler(this, new ReloadFLMEventArgs(
                ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK,
                null, reloadMsg));

              if (bytesProcessed != bytesReceived) {
                long bytesProcessedTotal = 0;
                string lastMsgType = "";
                while (reloadMsg != null
                  && bytesProcessedTotal < bytesReceived) {
                  //in - offset out - bytesprocessed
                  bytesProcessed = bytesProcessedTotal;
                  //TKTODO add framing handling  here
                  reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(
                    buffer, ref bytesProcessed, ReloadMessage.ReadFlags.full);
                  // Massive HACK!!! offset of TCP messages is set wrong TODO!!!
                  int offset = 0;
                  while (reloadMsg == null) {
                    offset++;
                    bytesProcessedTotal++;
                    bytesProcessed = bytesProcessedTotal;
                    reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(
                      buffer, ref bytesProcessed, ReloadMessage.ReadFlags.full);
                    if (reloadMsg != null)
                      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                        String.Format("Last message type: {0}, offset: {1}",
                        lastMsgType, offset));
                  }
                  ReloadFLMEventHandler(this, new ReloadFLMEventArgs(
                    ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK,
                    null, reloadMsg));
                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
                      String.Format("SimpleFLM: << {0} <== {1} {2}, TransID={3:x16}",
                      socketClient.RemoteEndPoint,
                      reloadMsg.reload_message_body.RELOAD_MsgCode.ToString(),
                      nodeid.ToString(),
                      reloadMsg.TransactionID));
                  bytesProcessedTotal += bytesProcessed;                  
                  lastMsgType = reloadMsg.reload_message_body.RELOAD_MsgCode.ToString();
                }
              }
            }
          }
          else {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("SimpleFLM: << {0} Receive: Dropping invalid packet,"+
              "bytes received: {1}", socketClient.RemoteEndPoint, bytesReceived));
          }
#endif
#if !CONNECTION_MANAGEMENT
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: << {0} Receive: Closing socket", client.RemoteEndPoint));
                    if(client.Connected)
                        client.Shutdown(SocketShutdown.Both);
                    client.Close();
#endif
        });
      }
    }

    private void HandleRemoteClosing(Socket client) {
      lock (m_connection_table) {
        foreach (KeyValuePair<String, SimpleOverlayConnectionTableElement> entry in m_connection_table) {
          if (entry.Value.AssociatedSocket == client) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
              String.Format("SimpleFLM: lost connection to {0} cleaning"+
              " connection table", entry));
            m_connection_table.Remove(entry.Key);
            m_ReloadConfig.ThisMachine.Transport.InboundClose(entry.Value.NodeID);
            break;
          }
        }
      }
    }

    public IEnumerator<ITask> Send(Node node, byte[] buffer) {
      Socket socket = null;
      SimpleOverlayConnectionTableElement socte = null;

#if false
            /* paranoia: check validity of buffer here again */
            if (   buffer[0] != 0xC2
                && buffer[1] != 0x45
                && buffer[2] != 0x4c
                && buffer[3] != 0x4f)
            {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SimpleFLM: Wrong Tag in Send message!");
                yield break;
            }
#endif

#if CONNECTION_MANAGEMENT
      /* beginn connection management */
      lock (m_connection_table) {
        if (node.Id != null && m_connection_table.ContainsKey(node.Id.ToString())) {
          SimpleOverlayConnectionTableElement el = m_connection_table[node.Id.ToString()];

          if (el.NodeID == node.Id) {
            if (el.AssociatedSocket.Connected) {
              long offset = 0;
              socket = el.AssociatedSocket;
              try {
                ReloadMessage reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(buffer, ref offset, ReloadMessage.ReadFlags.no_certcheck);
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: >> {0} {1} ==> {2}, TransID={3:x16}", socket.RemoteEndPoint, reloadMsg.reload_message_body.RELOAD_MsgCode.ToString(), node.Id.ToString(), reloadMsg.TransactionID));
                socte = el;
              }
              catch (Exception ex) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  String.Format("SimpleFLM Send: {0}", ex.Message));
              }
            }
            else {
              // no longer connected, remove entry
              m_connection_table.Remove(node.Id.ToString());
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: >> {0} removed, associated with {1}", el.AssociatedSocket, node.Id.ToString()));
            }
          }
        }
      }
      /* end connection management */
#endif

      if (socket == null) {
        if (node.IceCandidates != null) {
          /*                  if (node.Id != null)
                              {
                                  ReloadMessage reloadMsg = new ReloadMessage().FromBytes(buffer);
                                  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TEST, String.Format("SimpleFLM: {0} ==> {1} TransID={2:x16}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString(), node.Id.ToString(), reloadMsg.TransactionID));
                              }
          */
          foreach (IceCandidate candidate in node.IceCandidates) {
            switch (candidate.addr_port.type) {
              case AddressType.IPv6_Address:
              case AddressType.IPv4_Address: {
                  if (socket == null) {
                    socket = new Socket(candidate.addr_port.ipaddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                    var iarPort = new Port<IAsyncResult>();
                    socket.BeginConnect(new IPEndPoint(candidate.addr_port.ipaddr, candidate.addr_port.port), iarPort.Post, null);

                    yield return Arbiter.Receive(false, iarPort, iar => {
                      try {
                        socket.EndConnect(iar);
                      }
                      catch (Exception ex) {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SimpleFLM: Send Connect: " + ex.Message);
                        HandleRemoteClosing(socket);
                        if (socket.Connected)
                          socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                        socket = null;
                        return;
                      }
                    });
                  }
                }
                break;
            }
            //just support one ice candidate only here
            break;
          }
        }
        else {
          long offset = 0;
          ReloadMessage reloadMsg = new ReloadMessage(m_ReloadConfig).FromBytes(buffer, ref offset, ReloadMessage.ReadFlags.no_certcheck);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("SimpleFLM: {0} ==> {1} TransID={2:x16} (No ice candidates and no connection!)", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString(), node.Id.ToString(), reloadMsg.TransactionID));
          m_ReloadConfig.Statistics.IncConnectionError();
        }
      }

      if (socket != null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET,
          String.Format("SimpleFLM: >> {0} Send {1} bytes",
          socket.RemoteEndPoint, buffer.Length));

        var iarPort2 = new Port<IAsyncResult>();

        socte = new SimpleOverlayConnectionTableElement();

        byte[] framed_buffer = addFrameHeader(socte, buffer);

        socket.BeginSend(framed_buffer, 0, framed_buffer.Length,
          SocketFlags.None, iarPort2.Post, null);

        m_ReloadConfig.Statistics.BytesTx = (UInt64)framed_buffer.Length;

        yield return Arbiter.Receive(false, iarPort2, iar => {
          try {
            socket.EndSend(iar);
          }
          catch (Exception ex) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              "SimpleFLM: SocketError : " + ex.Message);
            m_ReloadConfig.Statistics.IncConnectionError();
          }
        });

#if CONNECTION_MANAGEMENT

        if (socket.Connected) {
          /* beginn connection management */

          lock (m_connection_table) {
            if (node.Id != null && node.Id != m_ReloadConfig.LocalNodeID)
              if (m_connection_table.ContainsKey(node.Id.ToString())) {
                SimpleOverlayConnectionTableElement rcel = m_connection_table[node.Id.ToString()];
                rcel.LastActivity = DateTime.Now;
              }
              else {
                SimpleOverlayConnectionTableElement rcel = new SimpleOverlayConnectionTableElement();
                rcel.NodeID = node.Id;
                rcel.AssociatedSocket = socket;
                rcel.LastActivity = rcel.Start = DateTime.Now;
                rcel.Outbound = true;

                m_connection_table.Add(rcel.NodeID.ToString(), rcel);
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_SOCKET, String.Format("SimpleFLM: >> {0} Send: Associating node {1}", socket.RemoteEndPoint, rcel.NodeID.ToString()));
                Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<Socket, NodeId>(socket, node.Id, Receive));
              }
          }
          if (node.Id == null) {
            Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<Socket, NodeId>(socket, node.Id, Receive));
          }
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SimpleFLM: Socket not connected after send");
        }
        /* end connection management */
#else
                if(socket.Connected)
                    socket.Shutdown(SocketShutdown.Both);
                socket.Close();
#endif
      }
    }

    internal void ShutDown() {
      foreach (KeyValuePair<string, SimpleOverlayConnectionTableElement> pair in m_connection_table) {
        Socket AssociatedSocket = pair.Value.AssociatedSocket;
        if (AssociatedSocket != null) {
          if (AssociatedSocket.Connected)
            AssociatedSocket.Shutdown(SocketShutdown.Both);
          AssociatedSocket.Close();
        }
      }

      lock (m_connection_table) {
        try {
          if (m_ListenerSocket.Connected)
            m_ListenerSocket.Shutdown(SocketShutdown.Both);
        }
        catch { };

        //this method calls dispose inside
        m_ListenerSocket.Close();
        //let CCR finish task to prevent ObjectDisposeException
        System.Threading.Thread.Sleep(250);
      }
    }


    public FramedMessageData FMD_FromBytes(byte[] bytes) {
      FramedMessageData fmdata;

      fmdata.sequence = 0;
      fmdata.type = 0;

      if (bytes == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: bytes = null!!");
      }

      MemoryStream ms = new MemoryStream(bytes);

      using (BinaryReader reader = new BinaryReader(ms)) {
        try {
          fmdata.type = (FramedMessageType)reader.ReadByte();
          fmdata.sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: " + ex.Message);
        }
      }
      return fmdata;
    }

    public FramedMessageAck FMA_FromBytes(byte[] bytes) {
      FramedMessageAck fmack;

      fmack.type = 0;
      fmack.ack_sequence = 0;
      fmack.received = 0;

      if (bytes == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: bytes = null!!");
      }

      MemoryStream ms = new MemoryStream(bytes);

      using (BinaryReader reader = new BinaryReader(ms)) {
        try {
          fmack.type = (FramedMessageType)reader.ReadByte();
          fmack.ack_sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
          fmack.ack_sequence = (UInt32)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
        }
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "FromBytes: " + ex.Message);
        }
      }
      return fmack;
    }

    public byte[] ToBytes(FramedMessageData fmdata, byte[] message) {
      MemoryStream ms = new MemoryStream();
      using (BinaryWriter writer = new BinaryWriter(ms)) {
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

    public byte[] ToBytes(FramedMessageAck fmack) {
      MemoryStream ms = new MemoryStream();
      using (BinaryWriter writer = new BinaryWriter(ms)) {
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
    private byte[] addFrameHeader(SimpleOverlayConnectionTableElement connectionTableEntry, byte[] message) {
      if (ReloadGlobals.Framing) {
        /* Add FH, manage connections */
        FramedMessageData fh_message_data = new FramedMessageData();
        fh_message_data.type = FramedMessageType.data;
        fh_message_data.sequence = connectionTableEntry.fh_sequence;

        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("Tx FH DATA {0}", connectionTableEntry.fh_sequence));

        connectionTableEntry.fh_sent.Add(connectionTableEntry.fh_sequence++, DateTime.Now);
        return ToBytes(fh_message_data, message);
      }
      else {
        return message;
      }
    }

    /// <summary>
    /// Analyse the frame header, calculate RTO.
    /// </summary>
    /// <param name="fh_message">The fh_message.</param>
    /// <param name="reload_connection">The reload_connection.</param>
    /// <returns></returns>
    private byte[] analyseFrameHeader(SimpleOverlayConnectionTableElement connectionTableEntry, byte[] fh_message) {
      if (ReloadGlobals.Framing) {
        /* Handle FrameHeader */
        FramedMessageType type = (FramedMessageType)fh_message[0];
        if (type == FramedMessageType.ack) {
          FramedMessageAck fh_ack = FMA_FromBytes(fh_message);

          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FH, String.Format("Rx FH ACK {0}, 0x{1:x}", fh_ack.ack_sequence, fh_ack.received));

          /* Calculate RTO */
          DateTime sent;
          if (connectionTableEntry.fh_sent.TryGetValue(fh_ack.ack_sequence, out sent)) {
            long rtt = DateTime.Now.Ticks - sent.Ticks;
            if (connectionTableEntry.srtt == 0.0) {
              connectionTableEntry.srtt = rtt;
              connectionTableEntry.rttvar = 0.5 * rtt;
              connectionTableEntry.rto = new TimeSpan(Convert.ToInt64(rtt + 4 * connectionTableEntry.rttvar));
            }
            else {
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
        else {
          if (type == FramedMessageType.data) {
            FramedMessageData fh_data = FMD_FromBytes(fh_message);
            byte[] fh_stripped_data = new byte[fh_message.Length - 8];
            Array.Copy(fh_message, 8, fh_stripped_data, 0, fh_message.Length - 8);

            UInt32 received = 0;
            UInt32 n = fh_data.sequence;
            /* Calculate FH received mask */
            foreach (UInt32 m in connectionTableEntry.fh_received) {
              if (m < n || m >= (n - 32)) {
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
            Send(new Node(connectionTableEntry.NodeID, null), ToBytes(fh_ack));

            return fh_stripped_data;
          }
        }
        return null;
      }
      else
        return fh_message;
    }
  }
}
