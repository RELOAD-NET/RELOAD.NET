﻿/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
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
using System.Net.Sockets;
using System.Net;
using Microsoft.Ccr.Core;

using SBSSLCommon;
using SBServer;
using SBUtils;
using SBX509;
using SBCustomCertStorage;
using SBClient;

using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;

namespace TSystems.RELOAD {
  public class ReloadConnectionTableEntry {
    public NodeId NodeID;
    public DateTime LastActivity;
    internal object secureObject;
    internal UInt32 fh_sequence;
    internal Queue<UInt32> fh_received = new Queue<UInt32>(32);
    internal Dictionary<UInt32, DateTime> fh_sent = new Dictionary<uint, DateTime>(32);
    internal object fh_sent_sync_object = new object();
    internal double srtt = 0.0;
    internal double rttvar;
    internal TimeSpan rto = new TimeSpan(0, 0, 3);    /* RFC 2988 start value */
  }

  public class ReloadConnectionTableInfoElement {
    public Socket AssociatedSocket;
    public NodeId NodeID;
    public TimeSpan RemainingUpTime;
    public DateTime LastActivity;
    public bool Outbound;
  }

  interface IAssociation {
    /* Socket associations */
    Socket AssociatedSocket { get; }
    byte[] InputBuffer { get; }
    int InputBufferOffset { get; set; }

    /* Watches TLS connection establishment */
    Port<bool> TLSConnectionOpen { get; set; }
    bool TLSConnectionIsOpen { get; set; }
    Queue<byte[]> TLSConnectionWaitQueue { get; set; }

    /* Partner node associations */
    NodeId RemoteNodeId { get; set; }

    /* SBB associations */
    void TLSDataAvailable();                /* Stuff socket rx data into TLS stack */
    void TLSSendData(byte[] message);       /* Stuff socket tx data into TLS stack */
    void TLSClose(bool silent);
  }

  public delegate ReloadFLMEventArgs ReloadFLMEvent(object sender, ReloadFLMEventArgs args);

  public class ReloadFLMEventArgs : EventArgs {
    public enum ReloadFLMEventTypes {
      RELOAD_EVENT_RECEIVE_OK,
      RELOAD_EVENT_FRAME_SEND_BUFFER,
      RELOAD_EVENT_FRAME_SEND_OK,
      RELOAD_EVENT_STATUS_CONNECT_FAILED,
      RELOAD_EVENT_STATUS_CONNECT_LOSS
    }
    public ReloadFLMEventArgs(ReloadFLMEventTypes event_type, ReloadConnectionTableEntry connection_table_entry, ReloadMessage reloadMsg) {
      this.eventtype = event_type;

      this.reloadMsg = reloadMsg;
      this.connection_table_entry = connection_table_entry;
    }
    private ReloadMessage reloadMsg;
    public ReloadMessage Message {
      get { return reloadMsg; }
      set { reloadMsg = value; }
    }
    private ReloadConnectionTableEntry connection_table_entry;
    public ReloadConnectionTableEntry ConnectionTableEntry {
      get { return connection_table_entry; }
    }
    private ReloadFLMEventTypes eventtype;
    public ReloadFLMEventTypes Eventtype {
      get { return eventtype; }
    }
  }

  #region Framing Header

  public enum FramedMessageType {
    data = 128,
    ack = 129,
  };

  public struct FramedMessageData {
    public FramedMessageType type;
    public UInt32 sequence;
  };

  public struct FramedMessageAck {
    public FramedMessageType type;
    public UInt32 ack_sequence;
    public UInt32 received;
  };

  #endregion
  /// <summary>
  /// The serving part of the "servent"
  /// </summary>
  internal class ReloadTLSServer : TElSecureServer, IAssociation {
    private Socket associatedSocket;
    private byte[] inputBuffer;
    private int inputBufferOffset;
    private NodeId remoteNodeId;
    public Port<bool> tlsConnectionOpen;
    private bool tlsConnectionIsOpen;
    private Queue<byte[]> tlsConnectionWaitQueue = new Queue<byte[]>();

    public Port<bool> TLSConnectionOpen {
      get { return tlsConnectionOpen; }
      set { tlsConnectionOpen = value; }
    }
    public Socket AssociatedSocket {
      get { return associatedSocket; }
      set { associatedSocket = value; }
    }
    public NodeId RemoteNodeId {
      get { return remoteNodeId; }
      set { remoteNodeId = value; }
    }
    public byte[] InputBuffer {
      get { return inputBuffer; }
    }
    public int InputBufferOffset {
      get { return inputBufferOffset; }
      set { inputBufferOffset = value; }
    }
    public void TLSDataAvailable() {
      this.DataAvailable();
    }
    public void TLSSendData(byte[] message) {
      lock ("serverTLSSendLock")
        this.SendData(message);
    }
    public void TLSClose(bool silent) {
      this.Close(silent);
    }
    public bool TLSConnectionIsOpen {
      get { return tlsConnectionIsOpen; }
      set { tlsConnectionIsOpen = value; }
    }
    public Queue<byte[]> TLSConnectionWaitQueue {
      get { return tlsConnectionWaitQueue; }
      set { tlsConnectionWaitQueue = value; }
    }

    public ReloadTLSServer(Socket associatedSocket)
      : base(null) {
      this.associatedSocket = associatedSocket;
      this.inputBuffer = new byte[ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP];
      this.inputBufferOffset = 0;
    }
  }

  /// <summary>
  /// The client part of the "servent"
  /// </summary>
  internal class ReloadTLSClient : TElSecureClient, IAssociation {
    private Socket associatedSocket;
    private byte[] inputBuffer;
    private int inputBufferOffset;
    private NodeId remoteNodeId;
    private Port<bool> tlsConnectionOpen;
    private bool tlsConnectionIsOpen;
    private Queue<byte[]> tlsConnectionWaitQueue = new Queue<byte[]>();

    public Port<bool> TLSConnectionOpen {
      get { return tlsConnectionOpen; }
      set { tlsConnectionOpen = value; }
    }
    public Socket AssociatedSocket {
      get { return associatedSocket; }
      set { associatedSocket = value; }
    }
    public NodeId RemoteNodeId {
      get { return remoteNodeId; }
      set { remoteNodeId = value; }
    }
    public byte[] InputBuffer {
      get { return inputBuffer; }
    }
    public int InputBufferOffset {
      get { return inputBufferOffset; }
      set { inputBufferOffset = value; }
    }
    public void TLSDataAvailable() {
      this.DataAvailable();
    }
    public void TLSSendData(byte[] message) {
      lock ("clientTLSSendLock")
        this.SendData(message);
    }
    public void TLSClose(bool silent) {
      this.Close(silent);
    }
    public bool TLSConnectionIsOpen {
      get { return tlsConnectionIsOpen; }
      set { tlsConnectionIsOpen = value; }
    }
    public Queue<byte[]> TLSConnectionWaitQueue {
      get { return tlsConnectionWaitQueue; }
      set { tlsConnectionWaitQueue = value; }
    }

    public ReloadTLSClient(Socket associatedSocket)
      : base(null) {
      this.associatedSocket = associatedSocket;
      this.inputBuffer = new byte[ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP];
      this.inputBufferOffset = 0;
    }
  }

  internal class ReloadSendParameters {
    internal ReloadConnectionTableEntry connectionTableEntry;
    internal IPAddress destinationAddress;
    internal int port;
    internal byte[] buffer;
    internal bool frame;
    internal Port<bool> done;   //Port is used to signal that the connection attempt to the specified endpoint (ip,port) is finished
  }

  public interface IForwardLinkManagement {
    bool Init();
    //void Send(Node NextHopNode, ReloadMessage reloadMessage);
    IEnumerator<ITask> Send(Node NextHopNode, ReloadMessage reloadMessage); //--joscha needed for fragmentation
    void ShutDown();
    event ReloadFLMEvent ReloadFLMEventHandler;
    bool NextHopInConnectionTable(NodeId dest_node_id);
    List<ReloadConnectionTableInfoElement> ConnectionTable { get; }
  }

}
