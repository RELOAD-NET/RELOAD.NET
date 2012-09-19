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
using System.Net;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.ForwardAndLinkManagement
{
    class ReloadConnectionTable : Dictionary<string, ReloadConnectionTableEntry>
    {
        private string connectionMakeKey(object secure_object)
        {
            return (secure_object as IAssociation).RemoteNodeId.ToString();
/*          if (secure_object is ReloadTLSClient)
            {
                return String.Format("{0}-{1}", (secure_object as ReloadTLSClient).AssociatedSocket.LocalEndPoint, (secure_object as ReloadTLSClient).AssociatedSocket.RemoteEndPoint);
            }
            else
            {
                return String.Format("{0}-{1}", (secure_object as ReloadTLSServer).AssociatedSocket.RemoteEndPoint, (secure_object as ReloadTLSServer).AssociatedSocket.LocalEndPoint);
            }
 */     }

        internal ReloadConnectionTableEntry updateEntry(object secureObject)
        {
            string connectionKey = connectionMakeKey(secureObject);
            ReloadConnectionTableEntry connectionTableEntry = null;
            if (TryGetValue(connectionKey, out connectionTableEntry))
                connectionTableEntry.LastActivity = DateTime.Now;

            return connectionTableEntry;
        }

        internal ReloadConnectionTableEntry lookupEntry(NodeId node_id)
        {
            foreach (ReloadConnectionTableEntry currentConnectionTableEntry in Values)
            {
                if (currentConnectionTableEntry.NodeID == node_id)
                {
                    return currentConnectionTableEntry;
                }
            }
            return null;
        }

        internal ReloadConnectionTableEntry lookupEntry(IPEndPoint ip_endpoint)
        {
            foreach (KeyValuePair<string, ReloadConnectionTableEntry> pair in this)
            {
                if (pair.Key.Contains(ip_endpoint.ToString()))
                {
                    return pair.Value;
                }
            }
            return null;
        }
    }
}
