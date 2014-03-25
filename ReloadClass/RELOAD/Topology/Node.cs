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
using System.Net;
using System.Net.Sockets;
using TSystems.RELOAD.ForwardAndLinkManagement;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.Topology
{

    /* corresponds to the transport production.  New transports such as
             SCTP or [I-D.baset-tsvwg-tcp-over-udp] can be added be defining
             new Transport values in the IANA registry in Section 13.8    */

    public enum Overlay_Link
    {
        DTLS_UDP_SR = 1,
        TLS_TCP_with_FH = 2,
        DLTS_UDP_SR_NO_ICE = 3,
        TLS_TCP_FH_NO_ICE = 4
    }

    public enum CandType
    {
        // TCP ICE (markus)
        tcp_host = 1,
        tcp_srflx = 2,
        tcp_prflx = 5,
        tcp_relay = 4,
        tcp_nat = 6,
        tcp_udptunneled = 7,
        tcp_bootstrap = 8,

        //UDP
        udp_host = 9,
        udp_srflx = 10,
        udp_prflx = 11,
        udp_relay = 12
    }


    public enum AddressType
    {
        IPv4_Address = 1,
        IPv6_Address = 2
    }

    /* corresponds to the connection-address and port productions */
    public class IpAddressPort : ICloneable
    {
        public AddressType type;
        public IPAddress ipaddr;
        public UInt16 port;

        public IpAddressPort(AddressType type, IPAddress ipaddr, UInt16 port)
        {
            this.type = type;
            this.ipaddr = ipaddr;
            this.port = port;
        }

        public object Clone()
        {
            object temp = this.MemberwiseClone();

            IPAddress tempIPAddress = null;

            using (var ms = new System.IO.MemoryStream())
            {
                var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                formatter.Serialize(ms, this.ipaddr);
                ms.Position = 0;

                tempIPAddress = (IPAddress)formatter.Deserialize(ms);
            }

            ((IpAddressPort)temp).ipaddr = tempIPAddress;

            return temp;
        }
    }

    public struct IceExtension
    {
        public Byte[] name;    /* length    <2^16-1> */
        public Byte[] value;   /* length    <2^16-1> */
    }

    public struct BootstrapServer
    {
        string m_Host;

        public string Host
        {
            get { return m_Host; }
            set { m_Host = value; }
        }
        int m_Port;

        public int Port
        {
            get { return m_Port; }
            set { m_Port = value; }
        }

        NodeId m_NodeId;

        public NodeId NodeId
        {
            get { return m_NodeId; }
            set { m_NodeId = value; }
        }

        public BootstrapServer(string Host, int Port)
        {
            m_Host = Host;
            m_Port = Port;
            m_NodeId = null;
        }
    }



    /// <summary>
    /// RELOAD ice candidates packed into a class
    /// </summary>
    public class IceCandidate : ICloneable
    {
        public IpAddressPort addr_port;
        public Overlay_Link overlay_link;
        public Byte[] foundation;  /* length: 0-255 */
        public UInt32 priority;
        public CandType cand_type;
        public IpAddressPort rel_addr_port; /* only if cand_type is not set to host */

        public List<IceExtension> extension_list = null;


        public IceCandidate()
        {
            this.cand_type = CandType.tcp_host;
        }

        public IceCandidate(IpAddressPort addr_port, Overlay_Link ol)
        {
            this.addr_port = addr_port;
            this.priority = IceCandidate.NoICE_PRIORITY;
            this.cand_type = CandType.tcp_host;
            this.overlay_link = ol;

            this.extension_list = new List<IceExtension>();

        }

        /* form one candidate with a priority value of 
           (2^24)*(126)+(2^8)*(65535)+(2^0)*(256-1)   */
        public static UInt32 NoICE_PRIORITY = 0x7EFFFFFF;

        public bool EqualsInAddressPort(IceCandidate iceCandidate)
        {
            // check if IP Address and Port are equal
            if (this.addr_port.ipaddr.Equals(iceCandidate.addr_port.ipaddr) &&
                this.addr_port.port == iceCandidate.addr_port.port &&
                this.addr_port.type == iceCandidate.addr_port.type)
            {
                return true;
            }

            else
                return false;
        }


        #region markus

        // active socket
        public Socket activeConnectingSocket;

        // passive sockets
        public Socket passiveListeningSocket;
        public Socket passiveAcceptedSocket;
        public Socket passiveSTUNSocket;

        // so sockets
        public Socket soListeningSocket;
        public Socket soAcceptedSocket;
        public Socket soConnectingSocket;
        public Socket soSTUN1Socket;
        public Socket soSTUN2Socket;


        // preferences
        public uint typePreference;
        public uint localPreference;
        public uint directionPreference;
        public uint otherPreference;

        // typ type
        public TcpType tcpType;

        #endregion

        public override bool Equals(object obj)
        {
            // If parameter is null return false.
            if (obj == null)
            {
                return false;
            }

            // If parameter cannot be cast to IceCandidate return false.
            IceCandidate cand = obj as IceCandidate;
            if ((System.Object)cand == null)
            {
                return false;
            }

            // Return true if the fields match:
            return (addr_port.port == cand.addr_port.port) &&
                    (priority == cand.priority) &&
                    (cand_type == cand.cand_type) &&
                    (tcpType == cand.tcpType);
        }

        public bool Equals(IceCandidate cand)
        {
            // If parameter is null return false:
            if ((object)cand == null)
            {
                return false;
            }

            // Return true if the fields match:
            return (addr_port.port == cand.addr_port.port) &&
                    (priority == cand.priority) &&
                    (cand_type == cand.cand_type) &&
                    (tcpType == cand.tcpType);
        }

        public override int GetHashCode()
        {
            int hash = 37;
            hash = hash * 23 + addr_port.port.GetHashCode();
            hash = hash * 23 + priority.GetHashCode();
            hash = hash * 23 + cand_type.GetHashCode();
            hash = hash * 23 + tcpType.GetHashCode();
            return hash;
            //return addr_port.port.GetHashCode() ^ priority.GetHashCode() ^ cand_type.GetHashCode() ^ tcpType.GetHashCode();
        }

        public static bool operator ==(IceCandidate a, IceCandidate b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            // If one is null, but not both, return false.
            if (((object)a == null) || ((object)b == null))
            {
                return false;
            }

            // Return true if the fields match:
            return a.Equals(b);
        }

        public static bool operator !=(IceCandidate a, IceCandidate b)
        {
            return !(a == b);
        }

        public object Clone()
        {
            IceCandidate tempCandidate = (IceCandidate)this.MemberwiseClone();
            tempCandidate.addr_port = (IpAddressPort)this.addr_port.Clone();

            if (this.rel_addr_port != null)
                tempCandidate.rel_addr_port = (IpAddressPort)this.rel_addr_port.Clone();

            return tempCandidate;
        }

    }

    /// <summary>
    /// Node maintenance uses this object
    /// </summary>
    public class Node
    {
        private NodeId m_Id;
        public NodeId Id
        {
            get { return m_Id; }
            set { m_Id = value; }
        }

        private List<NodeId> m_successors = new List<NodeId>();
        public List<NodeId> Successors
        {
            get { return m_successors; }
            set { m_successors = value; }
        }

        private List<NodeId> m_predecessors = new List<NodeId>();
        public List<NodeId> Predecessors
        {
            get { return m_predecessors; }
            set { m_predecessors = value; }
        }


        private List<IceCandidate> m_IceCandidates;
        public List<IceCandidate> IceCandidates
        {
            get { return m_IceCandidates; }
            set { m_IceCandidates = value; }
        }

        public Node(NodeId id, List<IceCandidate> ice_candidates)
        {
            m_IceCandidates = ice_candidates;
            m_Id = id;
        }

        public override string ToString()
        {
            if (m_IceCandidates != null && m_IceCandidates.Count > 0)
                return String.Format("{0} at {1}:{2}", Id, m_IceCandidates[0].addr_port.ipaddr.ToString(), m_IceCandidates[0].addr_port.port.ToString());
            else
                return String.Format("{0}", Id);
        }

        // markus
        private List<IceCandidate> m_NoIceCandidates;
        public List<IceCandidate> NoIceCandidates
        {
            get { return m_NoIceCandidates; }
            set { m_NoIceCandidates = value; }
        }


    }
}
