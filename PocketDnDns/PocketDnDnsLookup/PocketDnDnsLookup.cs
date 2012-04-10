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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Net.Sockets;
using System.Windows.Forms;

using System.Reflection;

using PocketDnDns.Enums;
using PocketDnDns.Query;
using PocketDnDns.Records;

namespace PocketDnDnsLookup
{
    public partial class PocketDnDnsLookup : Form
    {
        public PocketDnDnsLookup()
        {
            InitializeComponent();
        }

        private void PocketDnDnsLookup_Load(object sender, EventArgs e)
        {
            Type t = typeof(NsType);
            FieldInfo[] infos = t.GetFields(BindingFlags.Public | BindingFlags.Static);

            lstBxQueryType.DataSource = infos;
            lstBxQueryType.DisplayMember = "Name";
        }

        private void btnLookup_Click(object sender, EventArgs e)
        {
            NsType lookupType = (NsType)((FieldInfo)lstBxQueryType.SelectedValue).GetValue(null);

            DnsQueryRequest request = new DnsQueryRequest();

            DnsQueryResponse response = request.Resolve(txtDnsServer.Text, txtNameToLookup.Text, lookupType, NsClass.INET, ProtocolType.Udp);
            StringBuilder sb = new StringBuilder(1024);

            sb.Append("Bytes received: " + response.BytesReceived);
            sb.Append("\r\n");

            // Enumerate the Authoritive Name Servers Records
            sb.Append("Authoritive Name Servers:");
            sb.Append("\r\n");
            
            foreach (IDnsRecord record in response.AuthoritiveNameServers)
            {
                sb.Append(record.Answer);
                sb.Append("\r\n");
                sb.Append("  |--- RDATA Field Length: ");
                sb.Append(record.DnsHeader.DataLength);
                sb.Append("\r\n");
                sb.Append("  |--- Name: ");
                sb.Append(record.DnsHeader.Name);
                sb.Append("\r\n");
                sb.Append("  |--- NS Class: ");
                sb.Append(record.DnsHeader.NsClass);
                sb.Append("\r\n");
                sb.Append("  |--- NS Type: ");
                sb.Append(record.DnsHeader.NsType);
                sb.Append("\r\n");
                sb.Append("  |--- TTL: ");
                sb.Append(record.DnsHeader.TimeToLive);
                sb.Append("\r\n");
            }

            // Enumerate the Answer Records
            sb.Append("Answers:");
            sb.Append("\r\n");
            foreach (IDnsRecord record in response.Answers)
            {
                sb.Append(record.Answer);
                sb.Append("\r\n");
                sb.Append("  |--- RDATA Field Length: ");
                sb.Append(record.DnsHeader.DataLength);
                sb.Append("\r\n");
                sb.Append("  |--- Name: ");
                sb.Append(record.DnsHeader.Name);
                sb.Append("\r\n");
                sb.Append("  |--- NS Class: ");
                sb.Append(record.DnsHeader.NsClass);
                sb.Append("\r\n");
                sb.Append("  |--- NS Type: ");
                sb.Append(record.DnsHeader.NsType);
                sb.Append("\r\n");
                sb.Append("  |--- TTL: ");
                sb.Append(record.DnsHeader.TimeToLive);
                sb.Append("\r\n");
            }
            
            sb.Append("Additional Records");
            sb.Append("\r\n");
            foreach (IDnsRecord record in response.AdditionalRecords)
            {
                sb.Append(record.Answer);
                sb.Append("\r\n");
                sb.Append("  |--- RDATA Field Length: ");
                sb.Append(record.DnsHeader.DataLength);
                sb.Append("\r\n");
                sb.Append("  |--- Name: ");
                sb.Append(record.DnsHeader.Name);
                sb.Append("\r\n");
                sb.Append("  |--- NS Class: ");
                sb.Append(record.DnsHeader.NsClass);
                sb.Append("\r\n");
                sb.Append("  |--- NS Type: ");
                sb.Append(record.DnsHeader.NsType);
                sb.Append("\r\n");
                sb.Append("  |--- TTL: ");
                sb.Append(record.DnsHeader.TimeToLive);
                sb.Append("\r\n");
            }

            txtOutput.Text = sb.ToString();
        }
    }
}