/**********************************************************************
 * Copyright (c) 2008, j. montgomery                                  *
 * All rights reserved.                                               *
 *                                                                    *
 * Redistribution and use in source and binary forms, with or without *
 * modification, are permitted provided that the following conditions *
 * are met:                                                           *
 *                                                                    *
 * + Redistributions of source code must retain the above copyright   *
 *   notice, this list of conditions and the following disclaimer.    *
 *                                                                    *
 * + Redistributions in binary form must reproduce the above copyright*
 *   notice, this list of conditions and the following disclaimer     *
 *   in the documentation and/or other materials provided with the    *
 *   distribution.                                                    *
 *                                                                    *
 * + Neither the name of j. montgomery's employer nor the names of    *
 *   its contributors may be used to endorse or promote products      *
 *   derived from this software without specific prior written        *
 *   permission.                                                      *
 *                                                                    *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS*
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT  *
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS  *
 * FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE     *
 * COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,*
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES           *
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR *
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) *
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,*
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)      *
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED*
 * OF THE POSSIBILITY OF SUCH DAMAGE.                                 *
 **********************************************************************/
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using PocketDnDns.Enums;

namespace PocketDnDns.Query
{
	/// <summary>
    /// Summary description for DnsQueryRequest.
	/// </summary>
	public class DnsQueryRequest  : DnsQueryBase
	{
        private int _bytesSent = 0;
        
        /// <summary>
        /// The number of bytes sent to query the DNS Server.
        /// </summary>
        public int BytesSent
        {
            get { return _bytesSent; } 
        }

        #region Constructors
        public DnsQueryRequest() 
		{
            // Construct the class with some defaults
            _transactionId = 1;
            _flags = 0;
            _queryResponse = QueryResponse.Query;
            this._opCode = OpCode.QUERY;
            // Recursion Desired
            this._nsFlags = NsFlags.RD;
            this._questions = 1;
		}

		#endregion Constructors

		private byte[] BuildQuery(string host) 
		{
			string newHost;
			int newLocation = 0;
			int oldLocation = 0;

			MemoryStream ms = new MemoryStream();

			host = host.Trim();
			// decide how to build this query based on type
			switch (_nsType) 
			{
				case NsType.PTR:
					IPAddress queryIP = IPAddress.Parse(host);

					// pointer should be translated as follows
					// 209.115.22.3 -> 3.22.115.209.in-addr.arpa
					char[] ipDelim = new char[] {'.'};

					string[] s = host.Split(ipDelim);
					newHost = String.Format("{0}.{1}.{2}.{3}.in-addr.arpa", s[3], s[2], s[1], s[0]);
					break;
				default:
					newHost = host;
					break;
			}
			
			// Package up the host
			while(oldLocation < newHost.Length) 
			{
				newLocation = newHost.IndexOf(".", oldLocation);	
				
				if (newLocation == -1) newLocation = newHost.Length;

				byte subDomainLength = (byte)(newLocation - oldLocation);
				char[] sub = newHost.Substring(oldLocation, subDomainLength).ToCharArray();
				
				ms.WriteByte(subDomainLength);
				ms.Write(Encoding.ASCII.GetBytes(sub, 0, sub.Length), 0, sub.Length);

				oldLocation = newLocation + 1;
			}

			// Terminate the domain name w/ a 0x00. 
			ms.WriteByte(0x00);

			return ms.ToArray();
		}

        // TODO: Finish adding TCP Support
		/// <summary>
		/// 
		/// </summary>
		/// <param name="dnsServer"></param>
		/// <param name="host"></param>
		/// <param name="queryType"></param>
		/// <param name="queryClass"></param>
		/// <param name="protocol"></param>
        /// <returns>A <see cref="T:DnDns.Net.Dns.DnsQueryResponse"></see> instance that contains the Dns Answer for the request query.</returns>
        /// <PermissionSet>
        ///     <IPermission class="System.Net.DnsPermission, System, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Unrestricted="true" />
        /// </PermissionSet>
        public DnsQueryResponse Resolve(string dnsServer, string host, NsType queryType, NsClass queryClass, ProtocolType protocol) 
		{
            byte[] bDnsQuery = this.BuildDnsRequest(host, queryType, queryClass);
			
			// Connect to DNS server and get the record for the current server.
			IPAddress ipa = IPAddress.Parse(dnsServer);
			IPEndPoint ipep = new IPEndPoint(ipa, (int)UdpServices.Domain);
            
            byte[] recvBytes = null;

            // TODO: 
            switch (protocol)
            {
                case ProtocolType.Tcp:
                    {
                        throw new NotImplementedException("TCP Protocol implementation not finished.");
                        
                        //// Creates a SocketPermission which will allow the target Socket to connect with the DNS Server.
                        //SocketPermission socketPermission = new SocketPermission(
                        //    NetworkAccess.Connect, 
                        //    TransportType.Tcp, 
                        //    dnsServer, 
                        //    (int)UdpServices.Domain
                        //);
                        
                        //// do stack walk and Demand all callers have the appropriate SocketPermissions.
                        //socketPermission.Demand();

                        TcpClient tcpClient = new TcpClient();

                        try
                        {
                            tcpClient.Connect(ipep);

                            NetworkStream netStream = tcpClient.GetStream();
                            BinaryReader netReader = new System.IO.BinaryReader(netStream);
                            //netStream.CanWrite)
                            netStream.Write(bDnsQuery, 0, bDnsQuery.Length);

                            while (!netStream.DataAvailable) ;

                            if (netStream.DataAvailable)
                            {
                                MemoryStream memStream = new MemoryStream(1024);
                                bool canRead = netStream.DataAvailable;
                                
                                while (canRead)
                                {
                                    int byteRead = netStream.ReadByte();
                                    byte[] result = BitConverter.GetBytes(byteRead);

                                    memStream.WriteByte(result[0]);
                                    canRead = netStream.DataAvailable;
                                }
                                recvBytes = memStream.GetBuffer();
                            }
                        }
                        finally
                        {
                            tcpClient.Close();
                        }

                        break;
                    }
                case ProtocolType.Udp:
                    {
                        // Creates a SocketPermission which will allow the target Socket to connect with the DNS Server.
                        //SocketPermission socketPermission = new SocketPermission(
                        //    NetworkAccess.Connect, 
                        //    TransportType.Udp, 
                        //    dnsServer, 
                        //    (int)UdpServices.Domain
                        //);

                        //// Do stack walk and Demand all callers have the appropriate SocketPermissions.
                        //socketPermission.Demand();

                        // UDP messages, data size = 512 octets or less
                        UdpClient udpClient = new UdpClient();

                        try
                        {
                            udpClient.Connect(ipep);
                            udpClient.Send(bDnsQuery, bDnsQuery.Length);

                            DateTime before = DateTime.Now;
                            int iTimeout = 50;

                            Boolean loop = true;
                            while (loop)
                            {
                                udpClient.Client.Poll(iTimeout, SelectMode.SelectRead);
                                DateTime after = DateTime.Now;
                                TimeSpan diff = after - before;

                                if (diff.TotalMilliseconds > 10000)
                                    break;

                                if (udpClient.Client.Available > 0)
                                {
                                    recvBytes = udpClient.Receive(ref ipep);
                                    break;
                                }
                            }
                        }
                        catch(Exception ex)
                        {
                            throw new InvalidOperationException("DnsQueryResponse: " + ex.Message);
                        }
                        finally
                        {
                            udpClient.Close();
                        }
                        break;
                    }
                default:
                    {
                        throw new InvalidOperationException("Invalid Protocol: " + protocol.ToString());
                    }
            }

            if (recvBytes != null)
            {
                DnsQueryResponse dnsQR = new DnsQueryResponse();
                dnsQR.ParseResponse(recvBytes);
                return dnsQR;
            }
            return null;
		}

        private byte[] BuildDnsRequest(string host, NsType queryType, NsClass queryClass)
        {
            // Combind the NsFlags with our constant flags
            ushort flags = (ushort)((ushort)_queryResponse | (ushort)_opCode | (ushort)_nsFlags);
            this._flags = flags;

            this._nsType = queryType;
            this._nsClass = queryClass;

            byte[] flagBytes = new byte[2];
            byte[] transactionId = new byte[2];
            byte[] questions = new byte[2];
            byte[] answerRRs = new byte[2];
            byte[] authorityRRs = new byte[2];
            byte[] additionalRRs = new byte[2];
            byte[] nsType = new byte[2];
            byte[] nsClass = new byte[2];


            // Prepare data for over the wire transfer
            transactionId = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_transactionId) >> 16));
            flagBytes = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_flags) >> 16));
            questions = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_questions) >> 16));
            answerRRs = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_answerRRs) >> 16));
            authorityRRs = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_authorityRRs) >> 16));
            additionalRRs = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder(_additionalRRs) >> 16));
            nsType = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder((ushort)_nsType) >> 16));
            nsClass = BitConverter.GetBytes((ushort)(IPAddress.HostToNetworkOrder((ushort)_nsClass) >> 16));

            byte[] name = this.BuildQuery(host);

            // Build UPD DNS Packet to query
            MemoryStream ms = new MemoryStream();
            ms.Write(transactionId, 0, transactionId.Length);
            ms.Write(flagBytes, 0, flagBytes.Length);
            ms.Write(questions, 0, questions.Length);
            ms.Write(answerRRs, 0, answerRRs.Length);
            ms.Write(authorityRRs, 0, authorityRRs.Length);
            ms.Write(additionalRRs, 0, additionalRRs.Length);
            ms.Write(name, 0, name.Length);
            ms.Write(nsType, 0, nsType.Length);
            ms.Write(nsClass, 0, nsClass.Length);

            byte[] bDnsQuery = ms.ToArray();
            return bDnsQuery;
        }
	}
}
