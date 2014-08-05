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
using System.Net.Sockets;
using System.Net;
using Microsoft.Ccr.Core;
using System.Collections.Concurrent;

using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.ForwardAndLinkManagement;

namespace TSystems.RELOAD
{
    public class ReloadConnectionTableEntry
    {
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

    public class ReloadConnectionTableInfoElement
    {
        public Socket AssociatedSocket;
        public NodeId NodeID;
        public TimeSpan RemainingUpTime;
        public DateTime LastActivity;
        public bool Outbound;
    }

    interface IAssociation
    {
        /* Socket associations */
        Socket AssociatedSocket { get; set; }
        TcpClient AssociatedClient { get; }
        System.Net.Security.SslStream AssociatedSslStream { get; }
        byte[] InputBuffer { get; set; }
        int InputBufferOffset { get; set; }

        /* Watches TLS connection establishment */
        Port<bool> TLSConnectionOpen { get; set; }
        bool TLSConnectionIsOpen { get; set; }
        Queue<byte[]> TLSConnectionWaitQueue { get; set; }

        /* Partner node associations */
        NodeId RemoteNodeId { get; set; }

        /* Prevent concurrent writing access */
        ConcurrentQueue<byte[]> WritePendingData { get; }
        bool SendingData { get; set; }
    }

    public delegate ReloadFLMEventArgs ReloadFLMEvent(object sender, ReloadFLMEventArgs args);

    public class ReloadFLMEventArgs : EventArgs
    {
        public enum ReloadFLMEventTypes
        {
            RELOAD_EVENT_RECEIVE_OK,
            RELOAD_EVENT_FRAME_SEND_BUFFER,
            RELOAD_EVENT_FRAME_SEND_OK,
            RELOAD_EVENT_STATUS_CONNECT_FAILED,
            RELOAD_EVENT_STATUS_CONNECT_LOSS
        }
        public ReloadFLMEventArgs(ReloadFLMEventTypes event_type, ReloadConnectionTableEntry connection_table_entry, ReloadMessage reloadMsg)
        {
            this.eventtype = event_type;

            this.reloadMsg = reloadMsg;
            this.connection_table_entry = connection_table_entry;
        }
        private ReloadMessage reloadMsg;
        public ReloadMessage Message
        {
            get { return reloadMsg; }
            set { reloadMsg = value; }
        }
        private ReloadConnectionTableEntry connection_table_entry;
        public ReloadConnectionTableEntry ConnectionTableEntry
        {
            get { return connection_table_entry; }
        }
        private ReloadFLMEventTypes eventtype;
        public ReloadFLMEventTypes Eventtype
        {
            get { return eventtype; }
        }
    }

    #region Framing Header

    public enum FramedMessageType
    {
        data = 128,
        ack = 129,
    };

    public struct FramedMessageData
    {
        public FramedMessageType type;
        public UInt32 sequence;
    };

    public struct FramedMessageAck
    {
        public FramedMessageType type;
        public UInt32 ack_sequence;
        public UInt32 received;
    };

    #endregion
    /// <summary>
    /// The serving part of the "servent"
    /// </summary>
    internal class ReloadTLSServer : IAssociation
    {
        private Socket associatedSocket;
        private TcpClient associatedClient;
        private System.Net.Security.SslStream associatedSslStream;
        private byte[] inputBuffer;
        private int inputBufferOffset;
        private NodeId remoteNodeId;
        public Port<bool> tlsConnectionOpen;
        private bool tlsConnectionIsOpen;
        private Queue<byte[]> tlsConnectionWaitQueue = new Queue<byte[]>();
        private ConcurrentQueue<byte[]> writePendingData = new ConcurrentQueue<byte[]>();
        private bool sendingData = false;

        public ConcurrentQueue<byte[]> WritePendingData
        {
            get { return writePendingData; }
        }

        public bool SendingData
        {
            get { return sendingData; }
            set { sendingData = value; }
        }

        public Port<bool> TLSConnectionOpen
        {
            get { return tlsConnectionOpen; }
            set { tlsConnectionOpen = value; }
        }
        public System.Net.Security.SslStream AssociatedSslStream
        {
            get { return associatedSslStream; }
            set { associatedSslStream = value; }
        }
        public Socket AssociatedSocket
        {
            get { return associatedSocket; }
            set { associatedSocket = value; }
        }
        public TcpClient AssociatedClient
        {
            get { return associatedClient; }
            set { associatedClient = value; }
        }
        public NodeId RemoteNodeId
        {
            get { return remoteNodeId; }
            set { remoteNodeId = value; }
        }
        public byte[] InputBuffer
        {
            get { return inputBuffer; }
            set { inputBuffer = value; }
        }
        public int InputBufferOffset
        {
            get { return inputBufferOffset; }
            set { inputBufferOffset = value; }
        }
        public bool TLSConnectionIsOpen
        {
            get { return tlsConnectionIsOpen; }
            set { tlsConnectionIsOpen = value; }
        }
        public Queue<byte[]> TLSConnectionWaitQueue
        {
            get { return tlsConnectionWaitQueue; }
            set { tlsConnectionWaitQueue = value; }
        }

        public ReloadTLSServer(Socket associatedSocket)
        {
            this.associatedSocket = associatedSocket;
            this.inputBuffer = new byte[ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP];
            this.inputBufferOffset = 0;
        }
    }

    /// <summary>
    /// The client part of the "servent"
    /// </summary>
    internal class ReloadTLSClient : IAssociation
    {
        private Socket associatedSocket;
        private TcpClient associatedClient;
        private System.Net.Security.SslStream associatedSslStream;
        private byte[] inputBuffer;
        private int inputBufferOffset;
        private NodeId remoteNodeId;
        private Port<bool> tlsConnectionOpen;
        private bool tlsConnectionIsOpen;
        private Queue<byte[]> tlsConnectionWaitQueue = new Queue<byte[]>();
        private ConcurrentQueue<byte[]> writePendingData = new ConcurrentQueue<byte[]>();
        private bool sendingData = false;

        public ConcurrentQueue<byte[]> WritePendingData
        {
            get { return writePendingData; }
        }

        public bool SendingData
        {
            get { return sendingData; }
            set { sendingData = value; }
        }

        public Port<bool> TLSConnectionOpen
        {
            get { return tlsConnectionOpen; }
            set { tlsConnectionOpen = value; }
        }
        public Socket AssociatedSocket
        {
            get { return associatedSocket; }
            set { associatedSocket = value; }
        }
        public System.Net.Security.SslStream AssociatedSslStream
        {
            get { return associatedSslStream; }
            set { associatedSslStream = value; }
        }
        public TcpClient AssociatedClient
        {
            get { return associatedClient; }
            set { associatedClient = value; }
        }
        public NodeId RemoteNodeId
        {
            get { return remoteNodeId; }
            set { remoteNodeId = value; }
        }
        public byte[] InputBuffer
        {
            get { return inputBuffer; }
            set { inputBuffer = value; }
        }
        public int InputBufferOffset
        {
            get { return inputBufferOffset; }
            set { inputBufferOffset = value; }
        }
        public bool TLSConnectionIsOpen
        {
            get { return tlsConnectionIsOpen; }
            set { tlsConnectionIsOpen = value; }
        }
        public Queue<byte[]> TLSConnectionWaitQueue
        {
            get { return tlsConnectionWaitQueue; }
            set { tlsConnectionWaitQueue = value; }
        }

        public ReloadTLSClient(Socket associatedSocket)
        {
            this.associatedSocket = associatedSocket;
            this.inputBuffer = new byte[ReloadGlobals.MAX_PACKET_BUFFER_SIZE * ReloadGlobals.MAX_PACKETS_PER_RECEIVE_LOOP];
            this.inputBufferOffset = 0;
        }
    }

    // public necessary for use in GetConnectionQueue
    public class ReloadSendParameters
    {
        internal ReloadConnectionTableEntry connectionTableEntry;
        internal IPAddress destinationAddress;
        internal int port;
        internal byte[] buffer;
        internal bool frame;
        internal Port<bool> done;   //Port is used to signal that the connection attempt to the specified endpoint (ip,port) is finished

        // markus
        internal Socket connectionSocket;
    }

    public interface IForwardLinkManagement
    {
        bool Init();
        //void Send(Node NextHopNode, ReloadMessage reloadMessage);
        IEnumerator<ITask> Send(Node NextHopNode, ReloadMessage reloadMessage); //--joscha needed for fragmentation
        void ShutDown();
        event ReloadFLMEvent ReloadFLMEventHandler;
        bool NextHopInConnectionTable(NodeId dest_node_id);
        List<ReloadConnectionTableInfoElement> ConnectionTable { get; }

        // markus
        void StartReloadTLSServer(Socket socket);
        void StartReloadTLSClient(NodeId nodeid, Socket socket, IPEndPoint attacherEndpoint);
        void SaveConnection(CandidatePair choosenPair);
        Socket GetConnection(CandidatePair choosenPair);

        Util.ThreadSafeDictionary<IceCandidate, ReloadSendParameters> GetConnectionQueue();
    }

}
