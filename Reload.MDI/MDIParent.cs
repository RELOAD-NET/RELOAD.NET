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
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using TSystems.RELOAD;

namespace ReloadMDI
{
	/// <summary>
	/// Summary description for Form1.
	/// </summary>
	public class RELOADLauncher : System.Windows.Forms.Form
    {
		private System.Windows.Forms.MainMenu mainMenu1;
        private System.Windows.Forms.MenuItem menuItem5;
        private IContainer components;
		private System.Windows.Forms.MenuItem fileMenuItem;
		private System.Windows.Forms.MenuItem winMenuItem;
		private System.Windows.Forms.MenuItem newMenuItem;
		private System.Windows.Forms.MenuItem exitMenuItem;
		private System.Windows.Forms.MenuItem cascadeMenuItem;
		private System.Windows.Forms.MenuItem horizonMenuItem;
		private System.Windows.Forms.MenuItem verticalMenuItem;
        int childCount = 1;
        private MenuItem joinItem;
        private MenuItem leaveMenuItem;
        private MenuItem NodeMenuItem;
        private MenuItem menuItemShutDown;
        private MenuItem menuItemSettings;
        private TabControl tabCtrl;
        private ToolStrip toolStrip1;
        private ToolStripButton toolStripNewPeer;
        private ToolStripButton toolStripNewClient;
        private ToolStripButton toolStripJoin;
        private ToolStripButton toolStripLeaveOverlay;
        private ToolStripButton toolStripShutdown;
        private ToolStripButton toolStripFetch;
        private ToolStripButton toolStripStore;
        private TextBox textStore;
        private CheckBox checkBox1;
        private ToolStripButton toolStripClearDoc;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripLabel toolStripLabel1;
        private ToolStripButton toolStripRemoveData;
        private ToolStripButton toolStripInfo;
        private ToolStripButton toolStripGenerateCertificate;

        int m_iPortEnumerator = 0;

		public RELOADLauncher()
		{
			//
			// Required for Windows Form Designer support
			//
			InitializeComponent();
        }

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if (components != null) 
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RELOADLauncher));
            this.mainMenu1 = new System.Windows.Forms.MainMenu(this.components);
            this.fileMenuItem = new System.Windows.Forms.MenuItem();
            this.newMenuItem = new System.Windows.Forms.MenuItem();
            this.menuItemSettings = new System.Windows.Forms.MenuItem();
            this.exitMenuItem = new System.Windows.Forms.MenuItem();
            this.menuItem5 = new System.Windows.Forms.MenuItem();
            this.NodeMenuItem = new System.Windows.Forms.MenuItem();
            this.joinItem = new System.Windows.Forms.MenuItem();
            this.leaveMenuItem = new System.Windows.Forms.MenuItem();
            this.menuItemShutDown = new System.Windows.Forms.MenuItem();
            this.winMenuItem = new System.Windows.Forms.MenuItem();
            this.cascadeMenuItem = new System.Windows.Forms.MenuItem();
            this.horizonMenuItem = new System.Windows.Forms.MenuItem();
            this.verticalMenuItem = new System.Windows.Forms.MenuItem();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripNewPeer = new System.Windows.Forms.ToolStripButton();
            this.toolStripNewClient = new System.Windows.Forms.ToolStripButton();
            this.toolStripJoin = new System.Windows.Forms.ToolStripButton();
            this.toolStripLeaveOverlay = new System.Windows.Forms.ToolStripButton();
            this.toolStripShutdown = new System.Windows.Forms.ToolStripButton();
            this.toolStripStore = new System.Windows.Forms.ToolStripButton();
            this.toolStripFetch = new System.Windows.Forms.ToolStripButton();
            this.toolStripRemoveData = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripClearDoc = new System.Windows.Forms.ToolStripButton();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripInfo = new System.Windows.Forms.ToolStripButton();
            this.tabCtrl = new System.Windows.Forms.TabControl();
            this.textStore = new System.Windows.Forms.TextBox();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.toolStripGenerateCertificate = new System.Windows.Forms.ToolStripButton();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainMenu1
            // 
            this.mainMenu1.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.fileMenuItem,
            this.NodeMenuItem,
            this.winMenuItem});
            // 
            // fileMenuItem
            // 
            this.fileMenuItem.Index = 0;
            this.fileMenuItem.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.newMenuItem,
            this.menuItemSettings,
            this.exitMenuItem,
            this.menuItem5});
            this.fileMenuItem.Text = "&Program";
            // 
            // newMenuItem
            // 
            this.newMenuItem.Index = 0;
            this.newMenuItem.Shortcut = System.Windows.Forms.Shortcut.CtrlN;
            this.newMenuItem.Text = "&New Node";
            this.newMenuItem.Click += new System.EventHandler(this.NewMenuItem_Click);
            // 
            // menuItemSettings
            // 
            this.menuItemSettings.Index = 1;
            this.menuItemSettings.Text = "&Settings";
            this.menuItemSettings.Click += new System.EventHandler(this.menuItemSettings_Click);
            // 
            // exitMenuItem
            // 
            this.exitMenuItem.Index = 2;
            this.exitMenuItem.Text = "E&xit";
            this.exitMenuItem.Click += new System.EventHandler(this.exitMenuItem_Click);
            // 
            // menuItem5
            // 
            this.menuItem5.Index = 3;
            this.menuItem5.Text = "-";
            // 
            // NodeMenuItem
            // 
            this.NodeMenuItem.Index = 1;
            this.NodeMenuItem.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.joinItem,
            this.leaveMenuItem,
            this.menuItemShutDown});
            this.NodeMenuItem.Text = "&Node";
            // 
            // joinItem
            // 
            this.joinItem.Index = 0;
            this.joinItem.Text = "&Join";
            this.joinItem.Click += new System.EventHandler(this.joinItem_Click);
            // 
            // leaveMenuItem
            // 
            this.leaveMenuItem.Index = 1;
            this.leaveMenuItem.Text = "&Leave";
            this.leaveMenuItem.Click += new System.EventHandler(this.leaveMenuItem_Click);
            // 
            // menuItemShutDown
            // 
            this.menuItemShutDown.Index = 2;
            this.menuItemShutDown.Text = "&ShutDown";
            this.menuItemShutDown.Click += new System.EventHandler(this.menuItemShutDown_Click);
            // 
            // winMenuItem
            // 
            this.winMenuItem.Index = 2;
            this.winMenuItem.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.cascadeMenuItem,
            this.horizonMenuItem,
            this.verticalMenuItem});
            this.winMenuItem.Text = "&Window";
            // 
            // cascadeMenuItem
            // 
            this.cascadeMenuItem.Index = 0;
            this.cascadeMenuItem.Text = "&Cascade";
            this.cascadeMenuItem.Click += new System.EventHandler(this.cascadeMenuItem_Click);
            // 
            // horizonMenuItem
            // 
            this.horizonMenuItem.Index = 1;
            this.horizonMenuItem.Text = "Tile &Horizontal";
            this.horizonMenuItem.Click += new System.EventHandler(this.horizonMenuItem_Click);
            // 
            // verticalMenuItem
            // 
            this.verticalMenuItem.Index = 2;
            this.verticalMenuItem.Text = "Tile &Vertical";
            this.verticalMenuItem.Click += new System.EventHandler(this.verticalMenuItem_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripNewPeer,
            this.toolStripNewClient,
            this.toolStripJoin,
            this.toolStripLeaveOverlay,
            this.toolStripShutdown,
            this.toolStripStore,
            this.toolStripFetch,
            this.toolStripRemoveData,
            this.toolStripInfo,
            this.toolStripGenerateCertificate,
            this.toolStripSeparator1,
            this.toolStripClearDoc,
            this.toolStripLabel1});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(784, 25);
            this.toolStrip1.TabIndex = 3;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // toolStripNewPeer
            // 
            this.toolStripNewPeer.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripNewPeer.Image = ((System.Drawing.Image)(resources.GetObject("toolStripNewPeer.Image")));
            this.toolStripNewPeer.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripNewPeer.Name = "toolStripNewPeer";
            this.toolStripNewPeer.Size = new System.Drawing.Size(23, 22);
            this.toolStripNewPeer.Text = "New Peer";
            this.toolStripNewPeer.Click += new System.EventHandler(this.toolStripNewPeer_Click);
            // 
            // toolStripNewClient
            // 
            this.toolStripNewClient.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripNewClient.Image = ((System.Drawing.Image)(resources.GetObject("toolStripNewClient.Image")));
            this.toolStripNewClient.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripNewClient.Name = "toolStripNewClient";
            this.toolStripNewClient.Size = new System.Drawing.Size(23, 22);
            this.toolStripNewClient.Text = "NewClient";
            this.toolStripNewClient.ToolTipText = "New Client";
            this.toolStripNewClient.Click += new System.EventHandler(this.toolStripNewClient_Click);
            // 
            // toolStripJoin
            // 
            this.toolStripJoin.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripJoin.Image = ((System.Drawing.Image)(resources.GetObject("toolStripJoin.Image")));
            this.toolStripJoin.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripJoin.Name = "toolStripJoin";
            this.toolStripJoin.Size = new System.Drawing.Size(23, 22);
            this.toolStripJoin.Text = "Join";
            this.toolStripJoin.ToolTipText = "Join Overlay";
            this.toolStripJoin.Click += new System.EventHandler(this.toolStripJoin_Click);
            // 
            // toolStripLeaveOverlay
            // 
            this.toolStripLeaveOverlay.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripLeaveOverlay.Image = ((System.Drawing.Image)(resources.GetObject("toolStripLeaveOverlay.Image")));
            this.toolStripLeaveOverlay.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripLeaveOverlay.Name = "toolStripLeaveOverlay";
            this.toolStripLeaveOverlay.Size = new System.Drawing.Size(23, 22);
            this.toolStripLeaveOverlay.Text = "Leave";
            this.toolStripLeaveOverlay.ToolTipText = "Leave Overlay";
            this.toolStripLeaveOverlay.Click += new System.EventHandler(this.toolStripLeaveOverlay_Click);
            // 
            // toolStripShutdown
            // 
            this.toolStripShutdown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripShutdown.Image = ((System.Drawing.Image)(resources.GetObject("toolStripShutdown.Image")));
            this.toolStripShutdown.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripShutdown.Name = "toolStripShutdown";
            this.toolStripShutdown.Size = new System.Drawing.Size(23, 22);
            this.toolStripShutdown.Text = "Shutdown";
            this.toolStripShutdown.ToolTipText = "Shutdown Node";
            this.toolStripShutdown.Click += new System.EventHandler(this.toolStripShutdown_Click);
            // 
            // toolStripStore
            // 
            this.toolStripStore.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripStore.Image = ((System.Drawing.Image)(resources.GetObject("toolStripStore.Image")));
            this.toolStripStore.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripStore.Name = "toolStripStore";
            this.toolStripStore.Size = new System.Drawing.Size(23, 22);
            this.toolStripStore.Text = "Store";
            this.toolStripStore.Click += new System.EventHandler(this.toolStripStore_Click);
            // 
            // toolStripFetch
            // 
            this.toolStripFetch.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripFetch.Image = ((System.Drawing.Image)(resources.GetObject("toolStripFetch.Image")));
            this.toolStripFetch.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripFetch.Name = "toolStripFetch";
            this.toolStripFetch.Size = new System.Drawing.Size(23, 22);
            this.toolStripFetch.Text = "Fetch";
            this.toolStripFetch.Click += new System.EventHandler(this.toolStripFetch_Click);
            // 
            // toolStripRemoveData
            // 
            this.toolStripRemoveData.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripRemoveData.Image = ((System.Drawing.Image)(resources.GetObject("toolStripRemoveData.Image")));
            this.toolStripRemoveData.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripRemoveData.Name = "toolStripRemoveData";
            this.toolStripRemoveData.Size = new System.Drawing.Size(23, 22);
            this.toolStripRemoveData.Text = "Remove Data";
            this.toolStripRemoveData.Click += new System.EventHandler(this.toolStripRemoveData_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripClearDoc
            // 
            this.toolStripClearDoc.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripClearDoc.Image = ((System.Drawing.Image)(resources.GetObject("toolStripClearDoc.Image")));
            this.toolStripClearDoc.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripClearDoc.Name = "toolStripClearDoc";
            this.toolStripClearDoc.Size = new System.Drawing.Size(23, 22);
            this.toolStripClearDoc.Text = "Clear Document";
            this.toolStripClearDoc.ToolTipText = "Clear Document";
            this.toolStripClearDoc.Click += new System.EventHandler(this.toolStripClearDoc_Click);
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(37, 22);
            this.toolStripLabel1.Text = "Store:";
            // 
            // toolStripInfo
            // 
            this.toolStripInfo.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripInfo.Image = ((System.Drawing.Image)(resources.GetObject("toolStripInfo.Image")));
            this.toolStripInfo.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripInfo.Name = "toolStripInfo";
            this.toolStripInfo.Size = new System.Drawing.Size(23, 22);
            this.toolStripInfo.Text = "Info";
            this.toolStripInfo.Click += new System.EventHandler(this.toolStripInfo_Click);
            // 
            // tabCtrl
            // 
            this.tabCtrl.Dock = System.Windows.Forms.DockStyle.Top;
            this.tabCtrl.Font = new System.Drawing.Font("Arial", 8.25F, ((System.Drawing.FontStyle)((System.Drawing.FontStyle.Bold | System.Drawing.FontStyle.Italic))), System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tabCtrl.Location = new System.Drawing.Point(0, 25);
            this.tabCtrl.Name = "tabCtrl";
            this.tabCtrl.SelectedIndex = 0;
            this.tabCtrl.Size = new System.Drawing.Size(784, 24);
            this.tabCtrl.SizeMode = System.Windows.Forms.TabSizeMode.FillToRight;
            this.tabCtrl.TabIndex = 1;
            this.tabCtrl.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            // 
            // textStore
            // 
            this.textStore.Location = new System.Drawing.Point(307, 2);
            this.textStore.Name = "textStore";
            this.textStore.Size = new System.Drawing.Size(147, 20);
            this.textStore.TabIndex = 5;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Location = new System.Drawing.Point(460, 4);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(71, 17);
            this.checkBox1.TabIndex = 7;
            this.checkBox1.Text = "autoscroll";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // toolStripGenerateCertificate
            // 
            this.toolStripGenerateCertificate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripGenerateCertificate.Image = ((System.Drawing.Image)(resources.GetObject("toolStripGenerateCertificate.Image")));
            this.toolStripGenerateCertificate.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripGenerateCertificate.Name = "toolStripGenerateCertificate";
            this.toolStripGenerateCertificate.Size = new System.Drawing.Size(23, 22);
            this.toolStripGenerateCertificate.Text = "Generate Certificate";
            this.toolStripGenerateCertificate.Click += new System.EventHandler(this.toolStripGenerateCertificate_Click);
            // 
            // RELOADLauncher
            // 
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.ClientSize = new System.Drawing.Size(784, 841);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.textStore);
            this.Controls.Add(this.tabCtrl);
            this.Controls.Add(this.toolStrip1);
            this.IsMdiContainer = true;
            this.Menu = this.mainMenu1;
            this.Name = "RELOADLauncher";
            this.Text = "RELOAD Management Console";
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}
		#endregion

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main() 
		{
            ReloadGlobals.Client = ReloadMDI.Properties.Settings.Default.Client;
            ReloadGlobals.TLS = ReloadMDI.Properties.Settings.Default.TLS;
            ReloadGlobals.Framing = ReloadMDI.Properties.Settings.Default.Framing;
            ReloadGlobals.TimeStamps = ReloadMDI.Properties.Settings.Default.TimeStamps;
            ReloadGlobals.TLS_PASSTHROUGH = ReloadMDI.Properties.Settings.Default.TLS_Passtrough;
            ReloadGlobals.IgnoreSSLErrors = ReloadMDI.Properties.Settings.Default.IgnoreSSLErrors;
            ReloadGlobals.ReportEnabled = ReloadMDI.Properties.Settings.Default.ReportEnabled;
            ReloadGlobals.ReportIncludeConnections = ReloadMDI.Properties.Settings.Default.ReportIncludeConnections;
            ReloadGlobals.ReportIncludeFingers = ReloadMDI.Properties.Settings.Default.ReportIncludeFingers;
            ReloadGlobals.TRACELEVEL = (ReloadGlobals.TRACEFLAGS)ReloadMDI.Properties.Settings.Default.TraceLevel;
            ReloadGlobals.ForceLocalConfig = ReloadMDI.Properties.Settings.Default.ForceLocalConfig;
            ReloadGlobals.DNS_Address = ReloadMDI.Properties.Settings.Default.DNS_Address;
            //ReloadGlobals.OverlayName = ReloadMDI.Properties.Settings.Default.OverlayName;
            ReloadGlobals.ConfigurationServer = ReloadMDI.Properties.Settings.Default.ConfigurationServer;
            ReloadGlobals.IsVirtualServer = ReloadMDI.Properties.Settings.Default.IsVirtualServer;
            ReloadGlobals.AllowPrivateIP = ReloadMDI.Properties.Settings.Default.AllowPrivateIP;
            ReloadGlobals.MaxRetransmissions = ReloadMDI.Properties.Settings.Default.MaxRetransmissions;
            ReloadGlobals.SUCCESSOR_CACHE_SIZE = ReloadMDI.Properties.Settings.Default.SuccessorCacheSize;

            //we can't support multiple certs per commandline in this MDI app, we need enrollment server here
            if (ReloadGlobals.TLS)
                ReloadGlobals.ForceLocalConfig = false;

            Application.Run(new RELOADLauncher());
		}

		private void NewMenuItem_Click(object sender, System.EventArgs e)
		{
			//Creating MDI child form and initialize its fields
			MDIChild childForm = new MDIChild();
			childForm.Text = "Node " + childCount.ToString();
			childForm.MdiParent = this;

            if (tabCtrl.RowCount == 0)
                m_iPortEnumerator = ReloadMDI.Properties.Settings.Default.ListenPort;

			//Add a Tabpage and enables it
			TabPage tp = new TabPage();
			tp.Parent = tabCtrl;
			tp.Text = childForm.Text;
			tp.Show();

            childForm.TabCtrl = tabCtrl;
			//child Form will now hold a reference value to a tabpage
			childForm.TabPag = tp;

			//Activate the MDI child form
			childForm.Show();
			childCount++;

            childForm.StartReloadEngine(m_iPortEnumerator++);
            
            //Activate the newly created Tabpage
			tabCtrl.SelectedTab = tp;
		}

		private void exitMenuItem_Click(object sender, System.EventArgs e)
		{
			this.Close();
		}

		private void tabControl1_SelectedIndexChanged(object sender, System.EventArgs e)
		{
			foreach (MDIChild childForm in this.MdiChildren) 
			{
				//Check for its corresponding MDI child form
				if (childForm.TabPag.Equals(tabCtrl.SelectedTab)) 
				{
					//Activate the MDI child form
					childForm.Select();
                    break;
                }
			}
		}

		private void cascadeMenuItem_Click(object sender, System.EventArgs e)
		{
			this.LayoutMdi(MdiLayout.Cascade);
		}

		private void horizonMenuItem_Click(object sender, System.EventArgs e)
		{
			this.LayoutMdi(MdiLayout.TileHorizontal);
		}

		private void verticalMenuItem_Click(object sender, System.EventArgs e)
		{
			this.LayoutMdi(MdiLayout.TileVertical);
		}

        private void joinItem_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.RELOAD_Cmd_Join();
                    break;
                }
            }
        }

        private void leaveMenuItem_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.RELOAD_Cmd_Leave();
                    break;
                }
            }
        }

        private void menuItemShutDown_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.TabPag.Dispose();
                    childForm.Dispose();
                    break;
                }
            }
        }

        private void menuItemSettings_Click(object sender, EventArgs e)
        {
            ConfigForm config = new ConfigForm();
            config.ShowDialog();
        }

        private void toolStripNewPeer_Click(object sender, EventArgs e)
        {
            bool save = ReloadMDI.Properties.Settings.Default.Client;
            
            ReloadMDI.Properties.Settings.Default.Client = false;
            NewMenuItem_Click(sender, e);
            
            ReloadMDI.Properties.Settings.Default.Client = save;
        }

        private void toolStripNewClient_Click(object sender, EventArgs e)
        {
            bool save = ReloadMDI.Properties.Settings.Default.Client;
            
            ReloadMDI.Properties.Settings.Default.Client = true;
            NewMenuItem_Click(sender, e);

            ReloadMDI.Properties.Settings.Default.Client = save;
        }

        private void toolStripJoin_Click(object sender, EventArgs e)
        {
            joinItem_Click(sender, e);
        }

        private void toolStripLeaveOverlay_Click(object sender, EventArgs e)
        {
            leaveMenuItem_Click(sender, e);
        }

        private void toolStripShutdown_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.Finish();
                    break;
                }
            }
        }

        private void toolStripStore_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.SetE164(textStore.Text);
                    childForm.Store(textStore.Text);
                    break;
                }
            }
        }

        private void toolStripFetch_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.Fetch(textStore.Text);
                    break;
                }
            }
        }

        private void toolStripRemoveData_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.Remove();
                    break;
                }
            }
        }

        private void toolStripClearDoc_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.ClearDocument();
                    break;
                }
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            ReloadGlobals.DocumentAutoScroll = cb.Checked;
        }

        private void toolStripInfo_Click(object sender, EventArgs e)
        {
            foreach (MDIChild childForm in this.MdiChildren)
            {
                //Check for its corresponding MDI child form
                if (childForm.TabPag.Equals(tabCtrl.SelectedTab))
                {
                    childForm.Info();
                    break;
                }
            }
        }

        private void toolStripGenerateCertificate_Click(object sender, EventArgs e)
        {
            GCSR formGCSR = new GCSR();

            formGCSR.ShowDialog();
        }
	}
}
