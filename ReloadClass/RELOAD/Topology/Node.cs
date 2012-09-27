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
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.Topology {
    
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
        host = 1,
        srflx = 2,
        prflx = 3,
        relay = 4
    }


    public enum AddressType
    {
        IPv4_Address = 1,
        IPv6_Address = 2
    }

    /* corresponds to the connection-address and port productions */
    public class IpAddressPort
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

        public BootstrapServer(string Host, int Port)
        {
            m_Host = Host;
            m_Port = Port;
        }
    }

    /// <summary>
    /// RELOAD ice candidates packed into a class
    /// </summary>
    public class IceCandidate
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
            this.cand_type = CandType.host;
        }

        public IceCandidate(IpAddressPort addr_port, Overlay_Link ol)
        {
            this.addr_port    = addr_port;
            this.priority     = IceCandidate.NoICE_PRIORITY;
            this.cand_type    = CandType.host;
            this.overlay_link = ol;
        }

        /* form one candidate with a priority value of 
           (2^24)*(126)+(2^8)*(65535)+(2^0)*(256-1)   */
        public static UInt32 NoICE_PRIORITY = 0x7EFFFFFF;
    }

    /// <summary>
    /// Node maintenance uses this object
    /// </summary>
    public class Node {
        private NodeId m_Id;
        public NodeId Id {
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
        public List<IceCandidate> IceCandidates{
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

       
    }
}
