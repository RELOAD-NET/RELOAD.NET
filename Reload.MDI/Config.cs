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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ReloadMDI
{
    public partial class ConfigForm : Form
    {
        public ConfigForm()
        {
            InitializeComponent();
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {

            ReloadMDI.Properties.Settings.Default.Client = this.checkBoxClient.Checked;
            ReloadMDI.Properties.Settings.Default.TLS = this.checkBoxTLS.Checked;
            ReloadMDI.Properties.Settings.Default.Framing = this.checkBoxFraming.Checked;
            ReloadMDI.Properties.Settings.Default.IgnoreSSLErrors = this.checkBoxIgnoreSSL_Errors.Checked;
            ReloadMDI.Properties.Settings.Default.ReportEnabled = this.checkBoxReporting.Checked;
            ReloadMDI.Properties.Settings.Default.ReportIncludeConnections = this.checkBoxReportingIncludesConnections.Checked;
            ReloadMDI.Properties.Settings.Default.TraceLevel = Convert.ToInt32(this.textBoxTraceFlags.Text,16);
            ReloadMDI.Properties.Settings.Default.ListenPort = Convert.ToInt32(this.textBoxListenPort.Text);
            ReloadMDI.Properties.Settings.Default.DNS_Address = this.textBoxDNS_Server.Text;
            ReloadMDI.Properties.Settings.Default.ForceLocalConfig = this.checkBoxForceLocalConfig.Checked;

            global::ReloadMDI.Properties.Settings.Default.Save();

            DialogResult = DialogResult.OK;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            this.checkBoxForceLocalConfig.Checked = global::ReloadMDI.Properties.Settings.Default.ForceLocalConfig;
            this.checkBoxClient.Checked = global::ReloadMDI.Properties.Settings.Default.Client;
            this.checkBoxIgnoreSSL_Errors.Checked = global::ReloadMDI.Properties.Settings.Default.IgnoreSSLErrors;
            this.checkBoxReporting.Checked = global::ReloadMDI.Properties.Settings.Default.ReportEnabled;
            this.checkBoxReportingIncludesConnections.Checked = global::ReloadMDI.Properties.Settings.Default.ReportIncludeConnections;
            this.checkBoxTLS.Checked = global::ReloadMDI.Properties.Settings.Default.TLS;
            this.checkBoxFraming.Checked = global::ReloadMDI.Properties.Settings.Default.Framing;
            this.textBoxListenPort.Text = global::ReloadMDI.Properties.Settings.Default.ListenPort.ToString();
            this.textBoxDNS_Server.Text = global::ReloadMDI.Properties.Settings.Default.DNS_Address.ToString();
            this.textBoxTraceFlags.Text = "0x"+global::ReloadMDI.Properties.Settings.Default.TraceLevel.ToString("X8");
        }

    }
}
