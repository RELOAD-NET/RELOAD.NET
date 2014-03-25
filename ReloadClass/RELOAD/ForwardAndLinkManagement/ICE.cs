
using STUN;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Util;
using UPNP;

namespace TSystems.RELOAD.ForwardAndLinkManagement
{
    public enum TypePreference
    {
        // RFC 5245, ICE, Section 4.1.2.2:
        Host = 126,
        ServerReflexive = 100,
        PeerReflexive = 110,
        Relay = 0,

        // RFC 6544, Section 4.2:
        NATAssisted = 105,
        UDPTunneled = 75
    }

    public enum TcpType
    {
        Active,
        Passive,
        SO
    }

    public enum CandidatePairState
    {
        // RFC 5245, ICE, Section 5.7.4
        Waiting,
        InProgress,
        Succeeded,
        Failed,
        Frozen
    }

    public enum CheckListState
    {
        // RFC 5245, ICE, Section 5.7.4
        Running,
        Completed,
        Failed
    }


    // extended class for Ice Candidates
    /*
    public class ExtendedIceCandidate : IceCandidate
    {
        // active socket
        public Socket activeConnectingSocket;

        // passive sockets
        public Socket passiveListeningSocket;
        public Socket passiveSTUNSocket;

        // so sockets
        public Socket soListeningSocket;
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


        public ExtendedIceCandidate()
            : base()
        {

        }

        public ExtendedIceCandidate(IpAddressPort addr_port, Overlay_Link ol)
            : base(addr_port, ol)
        {

        }

    }
     * */

    // Candidate Pair (RFC 5245, ICE, Section 5.7.1. Figure 6)
    public class CandidatePair
    {
        public IceCandidate localCandidate;
        public IceCandidate remoteCandidate;

        // default candidates are not signaled or utilized by RELOAD (RFC 6940, RELOAD, Section 6.5.1.5)
        public bool valid;
        public bool nominated;
        public CandidatePairState state;

        public long pairPriority;

        public byte[] pairFoundation;

        public CandidatePair(IceCandidate localCandidate, IceCandidate remoteCandidate)
        {
            this.localCandidate = localCandidate;
            this.remoteCandidate = remoteCandidate;

            this.valid = false;
            this.nominated = false;
            this.state = CandidatePairState.Frozen;

            this.pairPriority = 0;

            /* RFC 5245, ICE, Section 5.7.4
            Each candidate pair in the check list has a foundation and a state.
            The foundation is the combination of the foundations of the local and
            remote candidates in the pair.
            */
            if (localCandidate.foundation != null && remoteCandidate.foundation != null)
            {
                this.pairFoundation = new byte[localCandidate.foundation.Length + remoteCandidate.foundation.Length];
                // first copy local candidate foundation
                Array.Copy(localCandidate.foundation, 0, this.pairFoundation, 0, localCandidate.foundation.Length);
                // then copy remote candidate foundation
                Array.Copy(remoteCandidate.foundation, 0, this.pairFoundation, localCandidate.foundation.Length, remoteCandidate.foundation.Length);
            }
            else
                this.pairFoundation = null;
        }

        public bool EqualsInAddressPort(CandidatePair candidatePair)
        {
            // checks if the IP Address and the Port of the local and the remote candidate are equal
            if (this.localCandidate.EqualsInAddressPort(candidatePair.localCandidate) &&
                this.remoteCandidate.EqualsInAddressPort(candidatePair.remoteCandidate))
            {
                return true;
            }

            else
                return false;
        }

    }

    public class CheckList
    {
        public List<CandidatePair> candidatePairs;
        public CheckListState checkListState;

        public CheckList(List<CandidatePair> candidatePairs, CheckListState checkListState)
        {
            this.candidatePairs = candidatePairs;
            this.checkListState = checkListState;
        }
    }

    public class ConnectCallbackParams
    {
        public Socket socket;
        public ManualResetEvent mre;
        //public int index;

        public ConnectCallbackParams(Socket socket, ManualResetEvent mre/*, int index*/)
        {
            this.socket = socket;
            this.mre = mre;
            //this.index = index;
        }
    }


    public class ICE
    {
        // Socket Timeout
        private const int SOCKET_TIMEOUT = 1000;
        private const int CONNECT_RETRIES = 4; // Number of connection retries in PerformCheck

        #region Gathering Candidates

        // CANDIDATE METHODS
        public static List<IceCandidate> GatherCandidates()
        {
            List<IceCandidate> iceCandidates = new List<IceCandidate>();

            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());

            List<IPAddress> localIPAddresses = new List<IPAddress>();

            // get all local IPs
            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    localIPAddresses.Add(address);
            }


            // we have to set other-pref here, for later priority calculation
            // simply decrement other.pref for each IP Address
            /* see RFC 6544, Section 4.2:
            If any two candidates have the same type-preference and direction-
            pref, they MUST have a unique other-pref.  With this specification,
            this usually only happens with multi-homed hosts, in which case
            other-pref is the preference for the particular IP address from which
            the candidate was obtained.  When there is only a single IP address,
            this value SHOULD be set to the maximum allowed value (8191).
            */
            uint otherPref = 8191;  // start with max. value

            // gather candidates for each device
            for (int i = 0; i < localIPAddresses.Count; i++)
            {
                // Host candidates               
                GatherHostCandidates(iceCandidates, localIPAddresses[i]);

                // Server Reflexive candidates
                if (ReloadGlobals.UseSR)
                    GatherServerReflexiveCandidates(iceCandidates, localIPAddresses[i]);

                // NAT assisted candidates
                if (ReloadGlobals.UseUPnP)
                    GatherNATAssistedCandidates(iceCandidates, localIPAddresses[i]);

                // finally set other-pref for each candidate
                foreach (IceCandidate candidate in iceCandidates)
                {
                    candidate.otherPreference = otherPref;
                }

                // simply decrement otherPref for next device
                otherPref--;
            }


            // calculate foundation for each candidate
            foreach (IceCandidate candidate in iceCandidates)
            {
                candidate.foundation = ComputeFoundation(candidate);
            }


            return iceCandidates;
        }

        public static List<IceCandidate> PrioritizeCandidates(List<IceCandidate> iceCandidates)
        {
            List<IceCandidate> prioritizedCandidates = new List<IceCandidate>();

            // prioritize all candidates
            foreach (IceCandidate candidate in iceCandidates)
            {
                IceCandidate prioCand = CalculatePriority(candidate);

                if (prioCand != null)
                    prioritizedCandidates.Add(prioCand);
            }

            return prioritizedCandidates;
        }

        private static uint DetermineDirectionPreference(IceCandidate iceCandidate)
        {
            // RFC 6544, Section 4.2:
            /*
            The direction-pref MUST be between 0 and 7 (both inclusive), with 7
            being the most preferred.  It is RECOMMENDED that the host, UDP-tunneled, 
            and relayed TCP candidates have the direction-pref assigned as follows: 
            6 for active, 4 for passive, and 2 for S-O.  For the NAT-assisted and 
            server reflexive candidates, the RECOMMENDED values are: 6 for S-O, 
            4 for active, and 2 for passive.
             */

            // check for TCP extension
            if (iceCandidate.extension_list != null && iceCandidate.extension_list.Count == 1)
            {
                // Host, UDP-tunneled oder Relayed candidate
                if (iceCandidate.cand_type == CandType.tcp_host || iceCandidate.cand_type == CandType.tcp_udptunneled || iceCandidate.cand_type == CandType.tcp_relay)
                {
                    switch (Encoding.UTF8.GetString(iceCandidate.extension_list[0].value))
                    {
                        case "active":
                            return 6;
                        case "passive":
                            return 4;
                        case "so":
                            return 2;
                    }
                }

                // NAT-assisted oder Server Reflexive candidate
                else if (iceCandidate.cand_type == CandType.tcp_nat || iceCandidate.cand_type == CandType.tcp_srflx)
                {
                    switch (Encoding.UTF8.GetString(iceCandidate.extension_list[0].value))
                    {
                        case "active":
                            return 4;
                        case "passive":
                            return 2;
                        case "so":
                            return 6;
                    }
                }

                // no supported candidate
                else
                    return 0;
            }


            // no TCP extension specified
            return 0;

        }

        private static IceCandidate CalculatePriority(IceCandidate iceCandidate)
        {
            // set type preference
            switch (iceCandidate.cand_type)
            {
                case CandType.tcp_host:
                    iceCandidate.typePreference = (uint)TypePreference.Host;
                    break;

                case CandType.tcp_nat:
                    iceCandidate.typePreference = (uint)TypePreference.NATAssisted;
                    break;

                case CandType.tcp_prflx:
                    iceCandidate.typePreference = (uint)TypePreference.PeerReflexive;
                    break;

                case CandType.tcp_relay:
                    iceCandidate.typePreference = (uint)TypePreference.Relay;
                    break;

                case CandType.tcp_srflx:
                    iceCandidate.typePreference = (uint)TypePreference.ServerReflexive;
                    break;

                case CandType.tcp_udptunneled:
                    iceCandidate.typePreference = (uint)TypePreference.UDPTunneled;
                    break;

                // no supported candidate type
                default:
                    return null;

            }

            // RFC 6940 (RELOAD), Section 6.5.1.1:
            // Each ICE candidate is represented as an IceCandidate structure, which is a direct translation of the information from the ICE string
            // structures, with the exception of the component ID. Since there is only one component, it is always 1, and thus left out of the structure.
            // component ID : RELOAD specific => always 1

            // RFC 6544, Section 4.2:
            // local preference = (2^13) * direction-pref + other-pref
            iceCandidate.directionPreference = DetermineDirectionPreference(iceCandidate);

            // other-pref is set in GatherCandidates()
            // decremented for each IP Address (or device)
            // see RFC 6544, Section 4.2:
            /*
            If any two candidates have the same type-preference and direction-
            pref, they MUST have a unique other-pref.  With this specification,
            this usually only happens with multi-homed hosts, in which case
            other-pref is the preference for the particular IP address from which
            the candidate was obtained.  When there is only a single IP address,
            this value SHOULD be set to the maximum allowed value (8191).
             */

            // calculate localPreference
            iceCandidate.localPreference = ((uint)PowerOf2(13)) * iceCandidate.directionPreference + iceCandidate.otherPreference;


            // calculate priority
            // RFC 5245, Section 4.1.2.1, formula:
            // priority = (2^24)*(type preference) + (2^8)*(local preference) + (2^0)*(256 - component ID)
            iceCandidate.priority = ((uint)PowerOf2(24)) * (iceCandidate.typePreference) + ((uint)PowerOf2(8)) * (iceCandidate.localPreference) + 255;


            return iceCandidate;
        }

        private static byte[] ComputeFoundation(IceCandidate candidate)   // checked
        {
            /* 
               RFC 5245, ICE, Section 4.1.1.3
               Finally, the agent assigns each candidate a foundation.  The
               foundation is an identifier, scoped within a session.  Two candidates
               MUST have the same foundation ID when all of the following are true:

               o  they are of the same type (host, relayed, server reflexive, or
                  peer reflexive).

               o  their bases have the same IP address (the ports can be different).

               o  for reflexive and relayed candidates, the STUN or TURN servers
                  used to obtain them have the same IP address.

               o  they were obtained using the same transport protocol (TCP, UDP,
                  etc.).

               Similarly, two candidates MUST have different foundations if their
               types are different, their bases have different IP addresses, the
               STUN or TURN servers used to obtain them have different IP addresses,
               or their transport protocols are different.
            */

            // type and transport protocol are both included in cand_type enum
            // and the candidates are always gathered from same servers (defined in config)
            // so we can build a foundation string out of the cand_type and the IP Address of the base, if a base exists (no host candidate)

            string candType = candidate.cand_type.ToString();
            string baseIPAddress = "";

            // if candidate has base (no host candidate)
            if (candidate.rel_addr_port != null)
                baseIPAddress = candidate.rel_addr_port.ipaddr.ToString();

            string foundation = candType + baseIPAddress;

            return Encoding.ASCII.GetBytes(foundation);
        }


        // HOST METHODS
        private static IceCandidate GatherHostActiveCandidate(IPAddress localIPAddress)
        {
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            //Socket activeConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //// here is no bind, because active candidates don't have to be physically allocated here (allocation in connectivity checks)
            //activeConnectingSocket.Bind(localEndpoint); // TEST

            /*  RFC 6544 (ICE TCP), Section 3:
            In the case of active candidates, both IP
            address and port are present, but the port is meaningless (it is
            there only for making encoding of active candidates consistent with
            the other candidate types and is ignored by the peer).  As a
            consequence, active candidates do not need to be physically allocated
            at the time of address gathering.  Rather, the physical allocations,
            which occur as a consequence of a connection attempt, occur at the
            time of the connectivity checks.
             */

            // save data in candidate object
            IpAddressPort activeIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            IceCandidate activeCandidate = new IceCandidate(activeIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            activeCandidate.cand_type = CandType.tcp_host;
            //activeCandidate.activeConnectingSocket = activeConnectingSocket; // TEST: in PerformCheck

            // TCP Type
            activeCandidate.tcpType = TcpType.Active;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("active");
            activeCandidate.extension_list.Add(iceExtension);

            return activeCandidate;
        }

        private static IceCandidate GatherHostPassiveCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);


            Socket passiveListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            passiveListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port
            localEndpoint = (IPEndPoint)passiveListeningSocket.LocalEndPoint;

            // save data in candidate object
            IpAddressPort passiveIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            IceCandidate passiveCandidate = new IceCandidate(passiveIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            passiveCandidate.cand_type = CandType.tcp_host;
            passiveCandidate.passiveListeningSocket = passiveListeningSocket;

            // TCP Type
            passiveCandidate.tcpType = TcpType.Passive;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("passive");
            passiveCandidate.extension_list.Add(iceExtension);

            return passiveCandidate;
        }

        private static IceCandidate GatherHostSOCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            // listening socket
            Socket soListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soListeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port
            localEndpoint = (IPEndPoint)soListeningSocket.LocalEndPoint;

            // connecting socket
            Socket soConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soConnectingSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soConnectingSocket.Bind(localEndpoint);

            // save data in candidate object
            IpAddressPort soIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            IceCandidate soCandidate = new IceCandidate(soIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            soCandidate.cand_type = CandType.tcp_host;
            soCandidate.soListeningSocket = soListeningSocket;
            soCandidate.soConnectingSocket = soConnectingSocket;

            // TCP Type
            soCandidate.tcpType = TcpType.SO;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("so");
            soCandidate.extension_list.Add(iceExtension);

            return soCandidate;
        }

        private static void GatherHostCandidates(List<IceCandidate> iceCandidates, IPAddress localIPAddress)
        {
            // HOST CANDIDATES
            // two candidates on different ports for each device
            // one for SO and one for PASSIVE


            // PASSIVE CANDIDATE 
            IceCandidate hostPassiveCandidate = GatherHostPassiveCandidate(localIPAddress);
            // add candidate to list
            if (hostPassiveCandidate != null)
                iceCandidates.Add(hostPassiveCandidate);


            // SO CANDIDATE
            //IceCandidate hostSOCandidate = GatherHostSOCandidate(localIPAddress);
            //// add candidate to list
            //if (hostSOCandidate != null)
            //    iceCandidates.Add(hostSOCandidate);


            // ACTIVE CANDIDATE: no real gathering, rather a placeholder. Inside the method the structure will be filled with local IP and IceExtension. No port allocation.
            /* RFC 6544, Section 4.1, top of Page 8:
            First, agents SHOULD obtain host candidates as described in
            Section 5.1.  Then, each agent SHOULD "obtain" (allocate a
            placeholder for) an active host candidate for each component of each
            TCP-capable media stream on each interface that the host has.  The
            agent does not yet have to actually allocate a port for these
            candidates, but they are used for the creation of the check lists.
             */

            IceCandidate hostActiveCandidate = GatherHostActiveCandidate(localIPAddress);
            // add candidate to list
            if (hostActiveCandidate != null)
                iceCandidates.Add(hostActiveCandidate);

        }


        // SERVER REFLEXIVE METHODS
        private static IceCandidate GatherSRActiveCandidate(IPAddress localIPAddress, IceCandidate baseCandidate)
        {
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            Socket activeConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // here is no bind, because active candidates don't have to be physically allocated here (allocation in connectivity checks)

            /*  RFC 6544 (ICE TCP), Section 3:
            In the case of active candidates, both IP
            address and port are present, but the port is meaningless (it is
            there only for making encoding of active candidates consistent with
            the other candidate types and is ignored by the peer).  As a
            consequence, active candidates do not need to be physically allocated
            at the time of address gathering.  Rather, the physical allocations,
            which occur as a consequence of a connection attempt, occur at the
            time of the connectivity checks.
             */

            // create candidate object
            /* RFC 6544, Section 5.2:
            Server reflexive active candidates
            can be derived from passive or S-O candidates by using the same IP
            addresses and interfaces as those candidates
             */
            IpAddressPort activeIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, baseCandidate.addr_port.ipaddr, 0);         // port is meaningless
            IceCandidate activeCandidate = new IceCandidate(activeIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            activeCandidate.cand_type = CandType.tcp_srflx;
            activeCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, 0);                      // port is meaningless
            activeCandidate.activeConnectingSocket = activeConnectingSocket;

            // TCP Type
            activeCandidate.tcpType = TcpType.Active;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("active");
            activeCandidate.extension_list.Add(iceExtension);

            return activeCandidate;

        }

        private static IceCandidate GatherSRPassiveCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            // listening socket
            Socket passiveListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            passiveListeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            passiveListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port
            localEndpoint = (IPEndPoint)passiveListeningSocket.LocalEndPoint;

            // STUN socket
            Socket passiveSTUNSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            passiveSTUNSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            passiveSTUNSocket.Bind(localEndpoint);

            // get defined STUN servers
            List<IPEndPoint> definedSTUNServers = GetDefinedSTUNServers();

            // servers defined?
            if (definedSTUNServers.Count > 0)
            {
                // use first server
                IPEndPoint STUNEndPoint = definedSTUNServers[0];

                // try to connect
                try
                {
                    passiveSTUNSocket.Connect(STUNEndPoint);
                }

                catch (Exception e1)
                {
                    //Console.WriteLine(e1.Message);

                    // use second STUN server, if defined
                    if (definedSTUNServers.Count == 2)
                    {
                        STUNEndPoint = definedSTUNServers[1];

                        // try to connect
                        try
                        {
                            passiveSTUNSocket.Connect(STUNEndPoint);
                        }

                        catch (Exception e2)
                        {
                            //Console.WriteLine(e2.Message);

                            // could not establish a connection to a STUN server
                            return null;
                        }
                    }
                }

            }

            // no STUN servers defined
            else
                return null;


            // get public mapping
            IPEndPoint publicMapping = GetPublicMapping(passiveSTUNSocket);


            // save data in candidate object
            IpAddressPort passiveIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, publicMapping.Address, (ushort)publicMapping.Port);
            IceCandidate passiveCandidate = new IceCandidate(passiveIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            passiveCandidate.cand_type = CandType.tcp_srflx;
            passiveCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            passiveCandidate.passiveListeningSocket = passiveListeningSocket;
            passiveCandidate.passiveSTUNSocket = passiveSTUNSocket;

            // TCP Type
            passiveCandidate.tcpType = TcpType.Passive;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("passive");
            passiveCandidate.extension_list.Add(iceExtension);

            return passiveCandidate;
        }

        private static IceCandidate GatherSRSOCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            // listening socket
            Socket soListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soListeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port
            localEndpoint = (IPEndPoint)soListeningSocket.LocalEndPoint;

            // connecting socket
            Socket soConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soConnectingSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soConnectingSocket.Bind(localEndpoint);

            // STUN1 socket
            Socket soSTUN1Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soSTUN1Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soSTUN1Socket.Bind(localEndpoint);

            // STUN2 socket
            Socket soSTUN2Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soSTUN2Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soSTUN2Socket.Bind(localEndpoint);


            // public mapping
            IPEndPoint publicMapping = null;


            // get defined STUN servers
            List<IPEndPoint> definedSTUNServers = GetDefinedSTUNServers();

            // no defined STUN servers
            if (definedSTUNServers.Count == 0)
                return null;


            // only one STUN Server defined
            // so we can't predict the port mapping for two successive outgoing connections
            // what we can do here is to assume that the outgoing connections get the same port mapping
            else if (definedSTUNServers.Count == 1)
            {
                // try to connect
                try
                {
                    soSTUN1Socket.Connect(definedSTUNServers[0]);
                }

                catch (Exception e)
                {
                    //Console.WriteLine(e.Message);

                    // could not connect to STUN server
                    return null;
                }

                publicMapping = GetPublicMapping(soSTUN1Socket);

            }

            // two STUN servers defined
            // here we can predict the port mapping for two successive outgoing connections
            else if (definedSTUNServers.Count == 2)
            {
                // try to connect to first STUN server
                try
                {
                    // get first public mapping
                    soSTUN1Socket.Connect(definedSTUNServers[0]);
                    IPEndPoint mapping1 = GetPublicMapping(soSTUN1Socket);

                    // now try to connect to second STUN Server
                    try
                    {
                        soSTUN2Socket.Connect(definedSTUNServers[1]);
                        IPEndPoint mapping2 = GetPublicMapping(soSTUN2Socket);

                        // calculate difference between port mappings
                        int portDifference = mapping2.Port - mapping1.Port;

                        // IP will be the same, port will be adapted
                        publicMapping = new IPEndPoint(mapping2.Address, mapping2.Port + portDifference);
                    }

                    catch (Exception e)
                    {
                        //Console.WriteLine(e.Message);

                        // use first mapping and assume port mapping will be equal
                        publicMapping = mapping1;
                    }

                }
                catch (Exception e1)
                {
                    //Console.WriteLine(e1.Message);

                    // could not connect to first STUN server
                    // try second, but then we can't predict port mapping, only assume the port mapping will be equal for next outgoing connection
                    try
                    {
                        soSTUN2Socket.Connect(definedSTUNServers[1]);

                        publicMapping = GetPublicMapping(soSTUN2Socket);
                    }

                    catch (Exception e2)
                    {
                        //Console.WriteLine(e2.Message);

                        // could not connect to first and second STUN Server
                        return null;
                    }
                }

            }


            // got no public mapping
            if (publicMapping == null)
                return null;


            // save data in candidate object
            IpAddressPort soIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, publicMapping.Address, (ushort)publicMapping.Port);
            IceCandidate soCandidate = new IceCandidate(soIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            soCandidate.cand_type = CandType.tcp_srflx;
            soCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            soCandidate.soListeningSocket = soListeningSocket;
            soCandidate.soConnectingSocket = soConnectingSocket;
            soCandidate.soSTUN1Socket = soSTUN1Socket;
            soCandidate.soSTUN2Socket = soSTUN2Socket;

            // TCP Type
            soCandidate.tcpType = TcpType.SO;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("so");
            soCandidate.extension_list.Add(iceExtension);


            return soCandidate;
        }

        private static void GatherServerReflexiveCandidates(List<IceCandidate> iceCandidates, IPAddress localIPAddress)
        {
            // SERVER REFLEXIVE CANDIDATES

            // PASSIVE CANDIDATE 
            IceCandidate srPassiveCandidate = GatherSRPassiveCandidate(localIPAddress);
            // add candidate to list
            if (srPassiveCandidate != null)
                iceCandidates.Add(srPassiveCandidate);


            // SO CANDIDATE
            //IceCandidate srSOCandidate = GatherSRSOCandidate(localIPAddress);
            //// add candidate to list
            //if (srSOCandidate != null)
            //    iceCandidates.Add(srSOCandidate);


            // ACTIVE CANDIDATE
            IceCandidate srActiveCandidate = null;
            /* RFC 6544, Section 5.2:
            Server reflexive active candidates
            can be derived from passive or S-O candidates by using the same IP
            addresses and interfaces as those candidates. 
            Furthermore, some techniques (e.g., TURN relaying) require knowing the IP address
            of the peer's active candidates beforehand, so active server
            reflexive candidates are needed for such techniques to function properly.
             */
            if (srPassiveCandidate != null)
            {
                srActiveCandidate = GatherSRActiveCandidate(localIPAddress, srPassiveCandidate);
            }
            //else if (srSOCandidate != null)
            //{
            //    srActiveCandidate = GatherSRActiveCandidate(localIPAddress, srSOCandidate);
            //}

            // add candidate to list
            if (srActiveCandidate != null)
                iceCandidates.Add(srActiveCandidate);

        }


        // NAT METHODS
        private static IceCandidate GatherNAActiveCandidate(IPAddress localIPAddress, IceCandidate baseCandidate)
        {
            // exactly the same procedure like GatherSRActiveCandidate()
            // derived from passive NAT assisted candidate

            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            Socket activeConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // here is no bind, because active candidates don't have to be physically allocated here (allocation in connectivity checks)

            /*  RFC 6544 (ICE TCP), Section 3:
            In the case of active candidates, both IP
            address and port are present, but the port is meaningless (it is
            there only for making encoding of active candidates consistent with
            the other candidate types and is ignored by the peer).  As a
            consequence, active candidates do not need to be physically allocated
            at the time of address gathering.  Rather, the physical allocations,
            which occur as a consequence of a connection attempt, occur at the
            time of the connectivity checks.
             */

            // create candidate object
            IpAddressPort activeIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, baseCandidate.addr_port.ipaddr, 0);         // port is meaningless
            IceCandidate activeCandidate = new IceCandidate(activeIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            activeCandidate.cand_type = CandType.tcp_nat;
            activeCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, 0);                      // port is meaningless
            activeCandidate.activeConnectingSocket = activeConnectingSocket;

            // TCP Type
            activeCandidate.tcpType = TcpType.Active;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("active");
            activeCandidate.extension_list.Add(iceExtension);

            return activeCandidate;
        }

        private static IceCandidate GatherNAPassiveCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            // only one listening socket, because there is no need to use a STUN server. We get the IP and Port from the NAT using UPnP
            Socket passiveListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            passiveListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port 
            localEndpoint = (IPEndPoint)passiveListeningSocket.LocalEndPoint;


            // check if UPnP is available
            UPnP upnp = new UPnP();
            bool foundUPnPDevice = false;

            try
            {
                foundUPnPDevice = upnp.Discover(localIPAddress);
            }
            catch (Exception e)
            {
                //Console.WriteLine(e.Message);
                return null;
            }


            // IPEndPoint for public mapping
            IPEndPoint publicMapping = null;

            // found UPnP internet gateway device?
            if (foundUPnPDevice)
            {
                // try to get public IP and to add a port mapping
                try
                {
                    string publicIP = upnp.GetExternalIP().ToString();

                    // we can use the local Port for the public NAT port mapping too
                    bool addedPortMapping = upnp.AddPortMapping((ushort)localEndpoint.Port, (ushort)localEndpoint.Port, ProtocolType.Tcp, "RELOAD ICE TCP Port mapping");

                    if (addedPortMapping && !string.IsNullOrEmpty(publicIP))
                    {
                        publicMapping = new IPEndPoint(IPAddress.Parse(publicIP), localEndpoint.Port);
                    }
                    else
                        return null;
                }

                catch (Exception e)
                {
                    //Console.WriteLine(e.Message);
                    return null;
                }
            }
            // no UPnP IGD found
            else
                return null;


            // got public mapping?
            if (publicMapping == null)
                return null;

            // save data in candidate object
            IpAddressPort passiveIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, publicMapping.Address, (ushort)publicMapping.Port);
            IceCandidate passiveCandidate = new IceCandidate(passiveIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            passiveCandidate.cand_type = CandType.tcp_nat;
            passiveCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            passiveCandidate.passiveListeningSocket = passiveListeningSocket;

            // TCP Type
            passiveCandidate.tcpType = TcpType.Passive;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("passive");
            passiveCandidate.extension_list.Add(iceExtension);


            return passiveCandidate;
        }

        private static IceCandidate GatherNASOCandidate(IPAddress localIPAddress)
        {
            // local endpoint
            IPEndPoint localEndpoint = new IPEndPoint(localIPAddress, 0);

            // listening socket
            Socket soListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soListeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soListeningSocket.Bind(localEndpoint);

            // update localEndpoint with binded Port 
            localEndpoint = (IPEndPoint)soListeningSocket.LocalEndPoint;

            // connecting socket
            Socket soConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            soConnectingSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            soConnectingSocket.Bind(localEndpoint);


            // check if UPnP is available
            UPnP upnp = new UPnP();
            bool foundUPnPDevice = false;

            try
            {
                foundUPnPDevice = upnp.Discover(localIPAddress);
            }
            catch (Exception e)
            {
                //Console.WriteLine(e.Message);
                return null;
            }


            // IPEndPoint for public mapping
            IPEndPoint publicMapping = null;

            // found UPnP internet gateway device?
            if (foundUPnPDevice)
            {
                // try to get public IP and to add a port mapping
                try
                {
                    string publicIP = upnp.GetExternalIP().ToString();

                    // we can use the local Port for the public NAT port mapping too
                    bool addedPortMapping = upnp.AddPortMapping((ushort)localEndpoint.Port, (ushort)localEndpoint.Port, ProtocolType.Tcp, "RELOAD ICE TCP Port mapping");

                    if (addedPortMapping && !string.IsNullOrEmpty(publicIP))
                    {
                        publicMapping = new IPEndPoint(IPAddress.Parse(publicIP), localEndpoint.Port);
                    }
                    else
                        return null;
                }

                catch (Exception e)
                {
                    //Console.WriteLine(e.Message);
                    return null;
                }
            }
            // no UPnP IGD found
            else
                return null;


            // got public mapping?
            if (publicMapping == null)
                return null;



            // save data in candidate object
            IpAddressPort soIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, publicMapping.Address, (ushort)publicMapping.Port);
            IceCandidate soCandidate = new IceCandidate(soIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            soCandidate.cand_type = CandType.tcp_nat;
            soCandidate.rel_addr_port = new IpAddressPort(AddressType.IPv4_Address, localEndpoint.Address, (ushort)localEndpoint.Port);
            soCandidate.soListeningSocket = soListeningSocket;
            soCandidate.soConnectingSocket = soConnectingSocket;

            // TCP Type
            soCandidate.tcpType = TcpType.SO;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("so");
            soCandidate.extension_list.Add(iceExtension);

            return soCandidate;
        }

        private static void GatherNATAssistedCandidates(List<IceCandidate> iceCandidates, IPAddress localIPAddress)
        {
            // PASSIVE CANDIDATE 
            IceCandidate naPassiveCandidate = GatherNAPassiveCandidate(localIPAddress);
            // add candidate to list
            if (naPassiveCandidate != null)
                iceCandidates.Add(naPassiveCandidate);


            // SO CANDIDATE
            // maybe there is a Software-Firewall installed, so we need SO candidates

            // worked passive candidate gathering? else we don't have to try SO gathering
            //if (naPassiveCandidate != null)
            //{
            //    IceCandidate naSOCandidate = null;

            //    naSOCandidate = GatherNASOCandidate(localIPAddress);

            //    // if SO gathering worked add candidate to list
            //    if (naSOCandidate != null)
            //        iceCandidates.Add(naSOCandidate);
            //}


            // ACTIVE CANDIDATE
            // derived from passive candidate (like server reflexive)
            //if (naPassiveCandidate != null)
            //{
            //    IceCandidate naActiveCandidate = null;

            //    naActiveCandidate = GatherNAActiveCandidate(localIPAddress, naPassiveCandidate);

            //    // add candidate to list
            //    if (naActiveCandidate != null)
            //        iceCandidates.Add(naActiveCandidate);
            //}
        }


        public static IceCandidate CreateBootstrapCandidate(IPAddress localIPAddress, int port)
        {
            // save data in candidate object
            IpAddressPort bootstrapIpAddressPort = new IpAddressPort(AddressType.IPv4_Address, localIPAddress, (ushort)port);
            IceCandidate bootstrapCandidate = new IceCandidate(bootstrapIpAddressPort, Overlay_Link.TLS_TCP_with_FH);
            bootstrapCandidate.cand_type = CandType.tcp_bootstrap;

            // TCP Type
            bootstrapCandidate.tcpType = TcpType.Passive;

            // TCP ICE extension
            IceExtension iceExtension = new IceExtension();
            iceExtension.name = Encoding.UTF8.GetBytes("tcptype");
            iceExtension.value = Encoding.UTF8.GetBytes("passive");
            bootstrapCandidate.extension_list.Add(iceExtension);


            // set other preference and foundation
            bootstrapCandidate.otherPreference = 8191;
            bootstrapCandidate.foundation = ComputeFoundation(bootstrapCandidate);

            return bootstrapCandidate;
        }

        public static List<IceCandidate> GatherActiveCandidatesForBootstrap()
        {
            List<IceCandidate> iceCandidates = new List<IceCandidate>();

            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());

            List<IPAddress> localIPAddresses = new List<IPAddress>();

            // get all local IPs
            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    localIPAddresses.Add(address);
            }

            uint otherPref = 8191;  // start with max. value

            // gather candidates for each device
            for (int i = 0; i < localIPAddresses.Count; i++)
            {
                // Host candidates               
                IceCandidate activeHost = GatherHostActiveCandidate(localIPAddresses[i]);

                // set other pref
                activeHost.otherPreference = otherPref;

                // add to list
                iceCandidates.Add(activeHost);

                // simply decrement otherPref for next device
                otherPref--;
            }


            // calculate foundation for each candidate
            foreach (IceCandidate candidate in iceCandidates)
            {
                candidate.foundation = ComputeFoundation(candidate);
            }


            return iceCandidates;

        }


        #endregion


        #region Forming Check List

        public static CheckList FormCheckList(List<IceCandidate> localCandidates, List<IceCandidate> remoteCandidates, bool localNodeIsControlling) // checked
        {
            List<CandidatePair> candidatePairs = FormingCandidatePairs(localCandidates, remoteCandidates);

            //PrintCandidatePairList(candidatePairs);

            candidatePairs = ComputePairPriority(candidatePairs, localNodeIsControlling);

            //PrintCandidatePairList(candidatePairs);

            candidatePairs = OrderPairsByPriority(candidatePairs);

            //PrintCandidatePairList(candidatePairs);

            candidatePairs = PruneCandidatePairs(candidatePairs);

            //PrintCandidatePairList(candidatePairs);

            candidatePairs = ComputeStates(candidatePairs);

            //PrintCandidatePairList(candidatePairs);

            // RFC 5245, ICE, Section 5.7.4
            // When a check list is first constructed as the consequence of an offer/answer exchange, it is placed in the Running state.
            return new CheckList(candidatePairs, CheckListState.Running);
        }

        private static List<CandidatePair> FormingCandidatePairs(List<IceCandidate> localCandidates, List<IceCandidate> remoteCandidates)   // checked
        {
            List<CandidatePair> candidatePairs = new List<CandidatePair>();

            /*  RFC 6544, ICE-TCP, Section 6.2:
            Local           Remote
            Candidate       Candidate
            ---------------------------
            tcp-so          tcp-so
            tcp-active      tcp-passive
            tcp-passive     tcp-active
            */

            foreach (IceCandidate localCandidate in localCandidates)
            {
                foreach (IceCandidate remoteCandidate in remoteCandidates)
                {
                    // in RELOAD there is no component ID in the ICE structure, since there is
                    // only one component, it is always 1, and thus left out of the structure.
                    // so we only have to check if both candidates have the same IP address version
                    if (localCandidate.addr_port.type == remoteCandidate.addr_port.type)
                    {
                        if (localCandidate.tcpType == TcpType.Active && remoteCandidate.tcpType == TcpType.Passive)
                        {
                            CandidatePair candidatePair = new CandidatePair(localCandidate, remoteCandidate);
                            candidatePairs.Add(candidatePair);
                        }

                        else if (localCandidate.tcpType == TcpType.Passive && remoteCandidate.tcpType == TcpType.Active)
                        {
                            CandidatePair candidatePair = new CandidatePair(localCandidate, remoteCandidate);
                            candidatePairs.Add(candidatePair);
                        }

                        else if (localCandidate.tcpType == TcpType.SO && remoteCandidate.tcpType == TcpType.SO)
                        {
                            CandidatePair candidatePair = new CandidatePair(localCandidate, remoteCandidate);
                            candidatePairs.Add(candidatePair);
                        }
                    }
                }
            }

            //PrintCandidatePairList(candidatePairs);

            if (candidatePairs.Count > 0)
                return candidatePairs;
            else
                return null;
        }

        private static List<CandidatePair> ComputePairPriority(List<CandidatePair> candidatePairs, bool localNodeIsControlling) // checked
        {
            /* RFC 5245, ICE, Section 5.7.2
            Let G be the priority for the candidate provided by the controlling
            agent.  Let D be the priority for the candidate provided by the
            controlled agent.  The priority for a pair is computed as:

            pair priority = 2^32*MIN(G,D) + 2*MAX(G,D) + (G>D?1:0)
            */
            uint G, D = 0;

            // if local Node is controlling => G gets local candidate priority
            if (localNodeIsControlling)
            {
                foreach (CandidatePair candidatePair in candidatePairs)
                {
                    G = candidatePair.localCandidate.priority;
                    D = candidatePair.remoteCandidate.priority;

                    candidatePair.pairPriority = (long)(PowerOf2(32) * Math.Min(G, D) + 2 * Math.Max(G, D) + (G > D ? 1 : 0));
                }
            }

            // if local Node is controlled => G gets remote candidate priority
            else
            {
                foreach (CandidatePair candidatePair in candidatePairs)
                {
                    G = candidatePair.remoteCandidate.priority;
                    D = candidatePair.localCandidate.priority;

                    candidatePair.pairPriority = (long)(PowerOf2(32) * Math.Min(G, D) + 2 * Math.Max(G, D) + (G > D ? 1 : 0));
                }
            }

            return candidatePairs;
        }

        private static List<CandidatePair> OrderPairsByPriority(List<CandidatePair> candidatePairs) // checked
        {
            List<CandidatePair> orderedCandidatePairs = null;

            try
            {
                orderedCandidatePairs = candidatePairs.OrderBy(o => o.pairPriority).ToList();
                // decreasing order of priority => reverse
                orderedCandidatePairs.Reverse();
            }
            catch (Exception e)
            {
                //Console.WriteLine(e.Message);
                return null;
            }

            return orderedCandidatePairs;
        }

        private static List<CandidatePair> PruneCandidatePairs(List<CandidatePair> candidatePairs)  // checked
        {
            /* RFC 5245, ICE, Section 5.7.3
            Since an agent cannot send requests directly from a reflexive candidate, but only from its base,
            the agent goes through the sorted list of candidate pairs.  For each pair where the local
            candidate is server reflexive, the server reflexive candidate MUST be replaced by its base
            */
            foreach (CandidatePair candidatePair in candidatePairs)
            {
                //if (candidatePair.localCandidate.cand_type == CandType.udp_srflx)   // only for UDP candidates? (not sure?)
                if (candidatePair.localCandidate.cand_type == CandType.udp_srflx || candidatePair.localCandidate.cand_type == CandType.tcp_srflx
                    || candidatePair.localCandidate.cand_type == CandType.tcp_nat)
                {
                    // rel_addr_port == base
                    candidatePair.localCandidate.addr_port = candidatePair.localCandidate.rel_addr_port;
                }
            }

            //PrintCandidatePairList(candidatePairs);

            // now pruning begins:
            /* RFC 5245, ICE, Section 5.7.3
            Once this has been done, the agent MUST prune the list.  This is done by removing a pair if its local and remote
            candidates are identical to the local and remote candidates of a pair higher up on the priority list.
            */
            candidatePairs = RemovePairDuplicates(candidatePairs);

            //PrintCandidatePairList(candidatePairs);


            /* RFC 6544, ICE-TCP, Section 6.2
            When the agent prunes the check list, it MUST also remove any pair for which the local candidate is a passive TCP candidate.  
            */
            candidatePairs = PruneLocalPassiveCandidates(candidatePairs);

            //PrintCandidatePairList(candidatePairs);



            /* RFC 6544, ICE-TCP, Section 6.2
            With pruning, the NAT-assisted candidates are treated like server reflexive candidates if the base is also used as a host candidate.
            */
            candidatePairs = TreatNACandidates(candidatePairs);

            // now we have to check for duplicates again and remove them
            candidatePairs = RemovePairDuplicates(candidatePairs);

            //PrintCandidatePairList(candidatePairs);

            return candidatePairs;
        }

        public static List<CandidatePair> RemovePairDuplicates(List<CandidatePair> candidatePairs)  // checked
        {
            List<int> positionsOfDuplicates = new List<int>();

            for (int i = candidatePairs.Count - 1; i > 0; i--)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    if (candidatePairs[j].EqualsInAddressPort(candidatePairs[i]))
                    {
                        positionsOfDuplicates.Add(i);
                        break;
                    }
                }
            }

            // found duplicates?
            if (positionsOfDuplicates.Count > 0)
            {
                for (int i = 0; i < positionsOfDuplicates.Count; i++)
                {
                    candidatePairs.RemoveAt(positionsOfDuplicates[i]);
                }
            }

            return candidatePairs;
        }

        private static List<CandidatePair> PruneLocalPassiveCandidates(List<CandidatePair> candidatePairs) // checked
        {
            // RFC 6544, ICE-TCP, Section 6.2
            // When the agent prunes the check list, it MUST also remove any pair for which the local candidate is a passive TCP candidate.

            // => we only have to check for duplicates of PASSIVE candidates, because SO candidates use REUSEADDRESS, and active candidates are not binded before the checks

            // save all pairs where local candidate is non passive
            var nonPassivePairs = candidatePairs.Where(pair => pair.localCandidate.tcpType != TcpType.Passive).ToList();

            // now get all pairs where local candidate is passive
            var passivePairs = candidatePairs.Where(pair => pair.localCandidate.tcpType == TcpType.Passive).ToList();

            // group all passive pairs by local IP and local Port
            var groupsWithSameIpAndPort = passivePairs.GroupBy(pair => new { pair.localCandidate.addr_port.ipaddr, pair.localCandidate.addr_port.port }).ToList();    // the "new { local IP, local PORT }" is our KEY, so we group our pairs in the list by this key

            // order all pairs in that groups by priority (highest priority first)            
            var groupsOrderedByPriority = groupsWithSameIpAndPort.Select(group => group.OrderBy(pair => pair.pairPriority).Reverse().ToList()).ToList();

            // now select each group and take the first pair from this group (the first pair is that one with the highest priority, because we ordered each pair in the groups by priority)
            var firstOfEachGroup = groupsOrderedByPriority.Select(group => group.First()).ToList();


            // now add all NON passive pairs and the SELECTED passive pairs to a new list
            List<CandidatePair> newCandidatePairs = new List<CandidatePair>();
            newCandidatePairs.AddRange(nonPassivePairs);
            newCandidatePairs.AddRange(firstOfEachGroup);

            // finally order all pairs by priority
            newCandidatePairs = newCandidatePairs.OrderBy(pair => pair.pairPriority).Reverse().ToList();

            return newCandidatePairs;
        }

        private static List<CandidatePair> TreatNACandidates(List<CandidatePair> candidatePairs)    // checked
        {
            /* RFC 6544, ICE-TCP, Section 6.2
            With pruning, the NAT-assisted candidates are treated like server reflexive candidates if the base is also used as a host candidate.
            
            RFC 5245, ICE, Section 5.7.3
            Since an agent cannot send requests directly from a reflexive candidate, but only from its base,
            the agent goes through the sorted list of candidate pairs.  For each pair where the local
            candidate is server reflexive, the server reflexive candidate MUST be replaced by its base
            */

            // first find all NAT assisted and host candidates in the PairList
            List<int> positionsOfNACandidates = new List<int>();
            List<int> positionsOfHostCandidates = new List<int>();

            for (int i = 0; i < candidatePairs.Count; i++)
            {
                if (candidatePairs[i].localCandidate.cand_type == CandType.tcp_nat)
                {
                    positionsOfNACandidates.Add(i);
                }
                else if (candidatePairs[i].localCandidate.cand_type == CandType.tcp_host)
                {
                    positionsOfHostCandidates.Add(i);
                }
            }

            // if no NAT assisted or host candidates were found, return untreated PairList
            if (positionsOfNACandidates.Count == 0 || positionsOfHostCandidates.Count == 0)
                return candidatePairs;


            //PrintCandidatePairList(candidatePairs);


            // iterate over all NAT assisted candidates
            for (int natIndex = 0; natIndex < positionsOfNACandidates.Count; natIndex++)
            {
                // iterate over all host candidates
                for (int hostIndex = 0; hostIndex < positionsOfHostCandidates.Count; hostIndex++)
                {
                    // check if NAT assisted candidate's base is also used as a host candidate
                    if (candidatePairs[positionsOfNACandidates[natIndex]].localCandidate.rel_addr_port.ipaddr == candidatePairs[positionsOfHostCandidates[hostIndex]].localCandidate.addr_port.ipaddr &&
                        candidatePairs[positionsOfNACandidates[natIndex]].localCandidate.rel_addr_port.port == candidatePairs[positionsOfHostCandidates[hostIndex]].localCandidate.addr_port.port)
                    {
                        // replace NAT assisted candidate's addr_port with rel_addr_port
                        candidatePairs[positionsOfNACandidates[natIndex]].localCandidate.addr_port = candidatePairs[positionsOfNACandidates[natIndex]].localCandidate.rel_addr_port;

                        // if one host candidate is found we don't have to find another host candidate
                        break;
                    }
                }
            }

            //PrintCandidatePairList(candidatePairs);

            return candidatePairs;
        }

        private static List<CandidatePair> ComputeStates(List<CandidatePair> candidatePairs)    // checked
        {
            /* RFC 5345, ICE, Section 5.7.4
            The initial states for each pair in a check list are computed by
            performing the following sequence of steps:

            1. The agent sets all of the pairs in each check list to the Frozen state.
            => Step 1 is already done by the Constructor of CandidatePair
             
            2. The agent examines the check list for the first media stream (a
            media stream is the first media stream when it is described by
            the first m line in the SDP offer and answer).  For that media
            stream:
                  For all pairs with the same foundation, it sets the state of
                  the pair with the lowest component ID to Waiting.  If there is
                  more than one such pair, the one with the highest priority is
                  used.
            */

            // RFC 6940, RELOAD, Section 6.5.1.3
            // Only a single media stream is supported.
            // RFC 6940, RELOAD, Section 6.5.1.1:
            // in RELOAD there is only one component, it is always 1


            // check if all pair foundations are != null
            foreach (CandidatePair candPair in candidatePairs)
                if (candPair.pairFoundation == null)
                    return null;

            // so we have to group elements of list by foundation (each group contains elements (=candidate pairs) with same foundation)
            // we use a string representation of the pair-foundation byte array, since strings can be easily compared in length and content
            var groups = candidatePairs.GroupBy(item => Encoding.ASCII.GetString(item.pairFoundation));

            // for each group ...
            foreach (var group in groups)
            {
                // ... sort elements (=pairs) by priority in descending order...
                var sortedGroup = group.OrderBy(item => item.pairPriority).Reverse().ToList();
                // ... and set the state of the pair with highest priority to Waiting state
                sortedGroup[0].state = CandidatePairState.Waiting;
            }


            return candidatePairs;
        }

        #endregion


        #region Connectivity Checks


        public static void ScheduleChecks(CheckList checkList, ReloadConfig.LogHandler logger)
        {
            // RFC 5245, ICE, Section 16.2:
            const int TA = 500;

            // Check Thread references
            List<Thread> checkThreads = new List<Thread>();

            // FIFO Queue for triggered checks
            List<CandidatePair> triggeredCheckQueue = new List<CandidatePair>();

            // order check list by pair-priority
            checkList.candidatePairs = checkList.candidatePairs.OrderBy(item => item.pairPriority).Reverse().ToList();

            while (true)
            {

                /* RFC 5245, ICE, Section 5.8:
                When the timer fires, the agent removes the top pair
                from the triggered check queue, performs a connectivity check on that
                pair, and sets the state of the candidate pair to In-Progress.  If
                there are no pairs in the triggered check queue, an ordinary check is
                sent.
                */

                // triggered check
                if (triggeredCheckQueue.Count > 0)
                {
                    // TODO !!!
                }

                // ordinary check
                // When the timer fires and there is no triggered check to be sent, the
                // agent MUST choose an ordinary check
                else
                {
                    // Find the highest-priority pair in that check list that is in the Waiting state, if there is one
                    CandidatePair waitingCandPair = null;
                    if (checkList.candidatePairs.Any(item => item.state == CandidatePairState.Waiting))
                    {
                        waitingCandPair = checkList.candidatePairs.First(item => item.state == CandidatePairState.Waiting);
                    }

                    // If there is such a pair
                    if (waitingCandPair != null)
                    {
                        // Send a STUN check from the local candidate of that pair to the
                        // remote candidate of that pair.  The procedures for forming the
                        // STUN request for this purpose are described in Section 7.1.2.

                        // Set the state of the candidate pair to In-Progress
                        waitingCandPair.state = CandidatePairState.InProgress;

                        Thread check = new Thread(() => PeformCheck(waitingCandPair, logger));
                        check.Start();
                        checkThreads.Add(check);

                        //// Set the state of the candidate pair to In-Progress
                        //waitingCandPair.state = CandidatePairState.InProgress;
                    }

                    // If there is no such pair
                    else
                    {
                        // Find the highest-priority pair in that check list that is in the Frozen state, if there is one
                        CandidatePair frozenCandPair = null;
                        if (checkList.candidatePairs.Any(item => item.state == CandidatePairState.Frozen))
                        {
                            frozenCandPair = checkList.candidatePairs.First(item => item.state == CandidatePairState.Frozen);
                        }

                        // If there is such a pair
                        if (frozenCandPair != null)
                        {
                            // Unfreeze the pair
                            frozenCandPair.state = CandidatePairState.Waiting;

                            // Set the state of the candidate pair to In-Progress
                            frozenCandPair.state = CandidatePairState.InProgress;

                            // Perform a check for that pair, causing its state to transition to In-Progress
                            Thread check = new Thread(() => PeformCheck(frozenCandPair, logger));
                            check.Start();
                            checkThreads.Add(check);

                            //// Set the state of the candidate pair to In-Progress
                            //frozenCandPair.state = CandidatePairState.InProgress;
                        }

                        // If there is no such pair
                        else
                        {
                            // Terminate the timer for that check list
                            break;
                        }
                    }

                }


                // wait N*TA (N = 1, just one media stream in RELOAD)
                Thread.Sleep(TA);
            }

            // wait for all threads
            foreach (Thread checkThread in checkThreads)
            {
                checkThread.Join();
            }
        }

        private static void PeformCheck(CandidatePair candPair, ReloadConfig.LogHandler logger)
        {
            // local active candidate
            if (candPair.localCandidate.tcpType == TcpType.Active)
            {
                // try to connect to remote candidate
                try
                {
                    //Socket connectingSocket = candPair.localCandidate.activeConnectingSocket;
                    IPEndPoint localEndPoint = new IPEndPoint(candPair.localCandidate.addr_port.ipaddr, candPair.localCandidate.addr_port.port);
                    Socket connectingSocket = null;

                    int retry = CONNECT_RETRIES;
                    while (retry > 0)
                    {
                        logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("PerformCheck connect to {0}:{1} retry {2}", candPair.remoteCandidate.addr_port.ipaddr.ToString(),
                            candPair.remoteCandidate.addr_port.port, retry));
                        connectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        connectingSocket.Bind(localEndPoint);

                        TimeoutSocket.Connect(connectingSocket, new IPEndPoint(candPair.remoteCandidate.addr_port.ipaddr, candPair.remoteCandidate.addr_port.port), ICE.SOCKET_TIMEOUT / CONNECT_RETRIES);
                        if (connectingSocket.Connected)
                            break;
                        retry--;
                    }

                    // connected?
                    if (connectingSocket.Connected)
                    {
                        // TEST: save connected socket
                        candPair.localCandidate.activeConnectingSocket = connectingSocket;
                        // TODO: send stun checks

                        // if stun check is successfull set state to succeeded
                        candPair.state = CandidatePairState.Succeeded;
                    }
                    else
                    {
                        candPair.state = CandidatePairState.Failed;
                    }
                }
                catch (Exception e)
                {
                    //Console.WriteLine(e.Message);

                    /* RFC 6544, ICE-TCP, Section 7.1:
                    If the TCP connection cannot be established, the check is considered
                    to have failed, and a full-mode agent MUST update the pair state to
                    Failed in the check list
                    */
                    candPair.state = CandidatePairState.Failed;
                }

            }

            // local passive candidate
            else if (candPair.localCandidate.tcpType == TcpType.Passive)
            {
                // try to accept a connection on listening port from remote peer
                try
                {
                    Socket listeningSocket = candPair.localCandidate.passiveListeningSocket;
                    listeningSocket.Listen(10);

                    logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("PerformCheck listen to {0}:{1}", ((IPEndPoint)listeningSocket.LocalEndPoint).Address.ToString(),
                        ((IPEndPoint)listeningSocket.LocalEndPoint).Port));

                    //Socket acceptedSocket = candPair.localCandidate.passiveAcceptedSocket;
                    candPair.localCandidate.passiveAcceptedSocket = TimeoutSocket.Accept(listeningSocket, ICE.SOCKET_TIMEOUT);

                    // connection accepted?
                    if (candPair.localCandidate.passiveAcceptedSocket != null && candPair.localCandidate.passiveAcceptedSocket.Connected)
                    {
                        // TODO: send stun checks

                        // if stun check is successfull set state to succeeded
                        candPair.state = CandidatePairState.Succeeded;
                    }
                    else
                    {
                        candPair.state = CandidatePairState.Failed;
                    }
                }
                catch (Exception e)
                {
                    candPair.state = CandidatePairState.Failed;
                }
            }

            // local so candidate
            else if (candPair.localCandidate.tcpType == TcpType.SO)
            {
                // try to connect to remote candidate and to accept a connection on listening port from remote peer
                try
                {
                    Socket connectingSocket = candPair.localCandidate.soConnectingSocket;
                    Socket listeningSocket = candPair.localCandidate.soListeningSocket;
                    listeningSocket.Listen(10);

                    // run Accept and Connect simultaneously
                    Socket acceptedSocket = candPair.localCandidate.soAcceptedSocket;
                    candPair.localCandidate.soAcceptedSocket = RunAcceptAndConnect(candPair, connectingSocket, listeningSocket);

                    if (connectingSocket.Connected || (acceptedSocket != null && acceptedSocket.Connected))
                    {
                        // TODO: send stun checks

                        // if stun check is successfull set state to succeeded
                        candPair.state = CandidatePairState.Succeeded;
                    }
                    else
                    {
                        candPair.state = CandidatePairState.Failed;
                    }

                }
                catch (Exception e)
                {
                    candPair.state = CandidatePairState.Failed;
                }
            }

        }

        private static Socket RunAcceptAndConnect(CandidatePair candPair, Socket connectingSocket, Socket listeningSocket)
        {
            // Connect() and Accept() must run simultaneously => use Threads
            Socket acceptedSocket = null;

            Thread acceptThread = new Thread(() => { acceptedSocket = TimeoutSocket.Accept(listeningSocket, ICE.SOCKET_TIMEOUT); });
            Thread connectThread = new Thread(() => { TimeoutSocket.Connect(connectingSocket, new IPEndPoint(candPair.remoteCandidate.addr_port.ipaddr, candPair.remoteCandidate.addr_port.port), ICE.SOCKET_TIMEOUT); });
            acceptThread.Start();
            connectThread.Start();

            acceptThread.Join();
            connectThread.Join();

            return acceptedSocket;
        }

        public static bool WaitForSignal(Socket socket)
        {
            // backup of receive timeout
            int receiveTimeout = socket.ReceiveTimeout;

            // wait one second for nomination signal
            socket.ReceiveTimeout = 2000;

            string nominatedString = "nominated";
            byte[] nominatedArray = Encoding.UTF8.GetBytes(nominatedString);
            byte[] buffer = new byte[255];

            int receivedBytes = 0;

            try
            {
                receivedBytes = socket.Receive(buffer);
            }
            catch (SocketException e)
            {
                // restore timeout
                socket.ReceiveTimeout = receiveTimeout;
                // socket exception after timeout
                return false;
            }

            byte[] receivedSignal = new byte[receivedBytes];
            Array.Copy(buffer, receivedSignal, receivedBytes);

            // restore timeout
            socket.ReceiveTimeout = receiveTimeout;

            // received "nominated" ?
            if (receivedBytes == nominatedArray.Length && Encoding.UTF8.GetString(receivedSignal).Equals(nominatedString))
                return true;
            else
                return false;
        }

        public static bool SendSignal(Socket socket, bool nominated)
        {
            // backup of receive timeout
            int sendTimeout = socket.SendTimeout;

            // wait one second for nomination signal
            socket.SendTimeout = 500;

            byte[] signal = null;

            if (nominated)
                signal = Encoding.UTF8.GetBytes("nominated");
            else
                signal = Encoding.UTF8.GetBytes("dismissed");


            int i = 0;

            try
            {
                i = socket.Send(signal);
            }
            catch (SocketException e)
            {
                // restore timeout
                socket.SendTimeout = sendTimeout;
                // socket exception after timeout
                return false;
            }

            // restore timeout
            socket.SendTimeout = sendTimeout;

            // sent all bytes?
            if (i == signal.Length)
                return true;
            else
                return false;
        }

        public static void CloseAllCandidateSockets(IceCandidate candidate)
        {
            if (candidate.activeConnectingSocket != null)
                candidate.activeConnectingSocket.Close();

            if (candidate.passiveAcceptedSocket != null)
                candidate.passiveAcceptedSocket.Close();

            if (candidate.passiveListeningSocket != null)
                candidate.passiveListeningSocket.Close();

            if (candidate.passiveSTUNSocket != null)
                candidate.passiveSTUNSocket.Close();

            if (candidate.soAcceptedSocket != null)
                candidate.soAcceptedSocket.Close();

            if (candidate.soConnectingSocket != null)
                candidate.soConnectingSocket.Close();

            if (candidate.soListeningSocket != null)
                candidate.soListeningSocket.Close();

            if (candidate.soSTUN1Socket != null)
                candidate.soSTUN1Socket.Close();

            if (candidate.soSTUN2Socket != null)
                candidate.soSTUN2Socket.Close();
        }

        #endregion


        #region Other Methods

        private static IPEndPoint GetPublicMapping(Socket stunSocket)
        {
            STUNMessage stunBindingRequest = new STUNMessage(StunMessageType.BindingRequest);
            stunBindingRequest.Create();

            stunSocket.Send(stunBindingRequest.ToByteArray());

            // STUN Header 20 Byte
            byte[] stunHeader = new byte[20];
            stunSocket.Receive(stunHeader, 20, SocketFlags.None);

            ushort stunBodyLength = NetworkByteArray.ReadUInt16(stunHeader, 2);
            byte[] stunBody = new byte[stunBodyLength];
            stunSocket.Receive(stunBody, stunBodyLength, SocketFlags.None);

            byte[] stunBindResp = new byte[20 + stunBodyLength];
            Array.Copy(stunHeader, 0, stunBindResp, 0, 20);
            Array.Copy(stunBody, 0, stunBindResp, 20, stunBodyLength);

            STUNMessage stunBindingResponse = STUNMessage.Parse(stunBindResp);

            // contains XOR Mapped Address?
            for (int i = 0; i < stunBindingResponse.AttributeList.Count; i++)
            {
                if (stunBindingResponse.AttributeList[i].Type == STUNAttribute.StunAttributeType.XorMappedAddress)
                {
                    XorMappedAddressAttribute xmaa = (XorMappedAddressAttribute)stunBindingResponse.AttributeList[i];
                    return xmaa.XorMappedAddress;

                }
            }

            // contains Mapped Address
            for (int i = 0; i < stunBindingResponse.AttributeList.Count; i++)
            {
                if (stunBindingResponse.AttributeList[i].Type == STUNAttribute.StunAttributeType.MappedAddress)
                {
                    MappedAddressAttribute maa = (MappedAddressAttribute)stunBindingResponse.AttributeList[i];
                    return maa.MappedAddress;
                }
            }

            // no attribute
            return null;
        }

        private static List<IPEndPoint> GetDefinedSTUNServers()
        {
            List<IPEndPoint> STUNServerList = new List<IPEndPoint>();

            IPEndPoint stunEndPoint = null;

            if (!String.IsNullOrEmpty(ReloadGlobals.StunIP1) && ReloadGlobals.StunPort1 != 0)
            {
                stunEndPoint = new IPEndPoint(IPAddress.Parse(ReloadGlobals.StunIP1), ReloadGlobals.StunPort1);
                STUNServerList.Add(stunEndPoint);
            }

            if (!String.IsNullOrEmpty(ReloadGlobals.StunIP2) && ReloadGlobals.StunPort2 != 0)
            {
                stunEndPoint = new IPEndPoint(IPAddress.Parse(ReloadGlobals.StunIP2), ReloadGlobals.StunPort2);
                STUNServerList.Add(stunEndPoint);
            }

            return STUNServerList;
        }

        private static long PowerOf2(int n)
        {
            return ((long)1 << n);
        }

        public static bool CompareByteArrays(Byte[] a, Byte[] b)
        {
            // same length?
            if (a.Length == b.Length)
            {
                return a.SequenceEqual(b);
            }

            else
                return false;
        }

        #endregion


        #region Output

        //public static void PrintCandidate(IceCandidate iceCandidate)
        //{
        //    Console.WriteLine("{0}:{1}", iceCandidate.addr_port.ipaddr.ToString(), iceCandidate.addr_port.port);
        //}

        public static void PrintCandidate(IceCandidate iceCandidate)
        {
            Console.WriteLine("{0}:{1} ({2})", iceCandidate.addr_port.ipaddr.ToString(), iceCandidate.addr_port.port, iceCandidate.tcpType.ToString());

            if (iceCandidate.rel_addr_port != null)
            {
                Console.WriteLine("\tBase: " + iceCandidate.rel_addr_port.ipaddr.ToString() + ":" + iceCandidate.rel_addr_port.port);
            }
        }

        public static void PrintCandidateList(List<IceCandidate> list)
        {
            foreach (IceCandidate cand in list)
                PrintCandidate(cand);

            Console.WriteLine("===================================================");
        }

        //public static void PrintCandidateList(List<IceCandidate> list)
        //{
        //    foreach (IceCandidate cand in list)
        //        PrintCandidate(cand);
        //    Console.WriteLine("===================================================");
        //}

        public static void PrintCandidatePair(CandidatePair candidatePair)
        {
            // mapping
            string local = candidatePair.localCandidate.addr_port.ipaddr.ToString() + ":" + candidatePair.localCandidate.addr_port.port +
                            " (" + candidatePair.localCandidate.tcpType.ToString() + " / " + candidatePair.localCandidate.cand_type.ToString() + ")";
            Console.WriteLine(local.PadRight(50, '.') + "{0}:{1} ({2} / {3})", candidatePair.remoteCandidate.addr_port.ipaddr.ToString(), candidatePair.remoteCandidate.addr_port.port,
                                                                                candidatePair.remoteCandidate.tcpType.ToString(), candidatePair.remoteCandidate.cand_type.ToString());

            // candidates have a base? (means no host)
            if (candidatePair.localCandidate.rel_addr_port != null && candidatePair.remoteCandidate.rel_addr_port != null)
            {
                string localBase = "Base: " + candidatePair.localCandidate.rel_addr_port.ipaddr.ToString() + ":" + candidatePair.localCandidate.rel_addr_port.port;
                Console.WriteLine(localBase.PadRight(50) + "Base: {0}:{1}", candidatePair.remoteCandidate.rel_addr_port.ipaddr.ToString(), candidatePair.remoteCandidate.rel_addr_port.port);
            }
            else if (candidatePair.localCandidate.rel_addr_port != null && candidatePair.remoteCandidate.rel_addr_port == null)
            {
                Console.WriteLine("Base: " + candidatePair.localCandidate.rel_addr_port.ipaddr.ToString() + ":" + candidatePair.localCandidate.rel_addr_port.port);
            }
            else if (candidatePair.localCandidate.rel_addr_port == null && candidatePair.remoteCandidate.rel_addr_port != null)
            {
                Console.WriteLine("".PadRight(50) + "Base: {0}:{1}", candidatePair.remoteCandidate.rel_addr_port.ipaddr.ToString(), candidatePair.remoteCandidate.rel_addr_port.port);
            }
        }

        public static void PrintCandidatePairList(List<CandidatePair> list)
        {
            foreach (CandidatePair pair in list)
            {
                PrintCandidatePair(pair);
                Console.WriteLine();
            }

            Console.WriteLine("".PadRight(100, '='));
        }

        #endregion

    }

}
