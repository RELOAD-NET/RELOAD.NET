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
using System.Text;
using System.Net;
using System.Net.Sockets;

using PocketDnDns.Enums;

namespace PocketDnDns
{
	public class Tools
	{
        /// <summary>
        /// Look up the port names for the given array of port numbers.
        /// </summary>
        /// <param name="port">An array of port numbers.</param>
        /// <param name="proto">The protocol type. (TCP or UPD)</param>
        /// <returns>The name of the port.</returns>
        public static string GetServByPort(short[] port, ProtocolType proto)
		{
			StringBuilder sb = new StringBuilder();
			for (int i=0; i < port.Length; i++)
			{
				sb.Append(GetServByPort(port[i], proto));
                sb.Append(", ");
			}
			sb.Remove(sb.Length-2,2);
			return sb.ToString();
		}

		/// <summary>
		/// Look up the port name for any given port number.
		/// </summary>
		/// <param name="port">The port number.</param>
		/// <param name="proto">The protocol type. (TCP or UPD)</param>
		/// <returns>The name of the port.</returns>
		public static string GetServByPort(short port, ProtocolType proto)
		{
			StringBuilder ans = new StringBuilder();

			switch (proto)
			{
				case ProtocolType.Tcp: 
				{
					TcpServices tcps;
					tcps = (TcpServices)port;
					ans.Append(tcps);
                    ans.Append("(");
                    ans.Append(port);
                    ans.Append(")");
					break;
				}
				case ProtocolType.Udp:
				{
					UdpServices udps;
					udps = (UdpServices)port;
                    ans.Append(udps);
                    ans.Append("(");
                    ans.Append(port);
                    ans.Append(")");
					break;
				}
				default:
				{
					ans.Append("(");
                    ans.Append(port);
                    ans.Append(")");
					break;
				}
			}
			return ans.ToString();
		}
	}
}