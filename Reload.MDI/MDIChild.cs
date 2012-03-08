/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012 Thomas Kluge <t.kluge@gmx.de> 
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
* Last edited by: Alex <alexander.knauf@gmail.com>
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using System.Diagnostics;
using TSystems.RELOAD;
using System.IO;

namespace ReloadMDI {
  /// <summary>
  /// Summary description for MDIChild.
  /// </summary>
  /// 

  public class RichTextBoxEx : RichTextBox {
    public delegate void UpdateTextDelegate(ReloadGlobals.TRACEFLAGS scope, string x);

    public void WriteLine(ReloadGlobals.TRACEFLAGS scope, string x) {
      if ((scope & ReloadGlobals.TRACELEVEL) != 0)
        if (!this.InvokeRequired) {
          if (ReloadGlobals.TimeStamps) {
            this.SelectionColor = Color.LightGray;
            this.AppendText(DateTime.Now.ToString("HH:mm:ss.fff  "));
          }

          switch (scope) {
            case ReloadGlobals.TRACEFLAGS.T_DATASTORE:
              this.SelectionColor = Color.Gray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_ERROR:
              this.SelectionColor = Color.Red;
              break;
            case ReloadGlobals.TRACEFLAGS.T_FH:
              this.SelectionColor = Color.Gray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_INFO:
              this.SelectionColor = Color.Gray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_SOCKET:
              this.SelectionColor = Color.DarkGray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_TLS:
              this.SelectionColor = Color.DarkGray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_TRANSPORT:
              this.SelectionColor = Color.DarkGray;
              break;
            case ReloadGlobals.TRACEFLAGS.T_TOPO:
              this.SelectionColor = Color.Blue;
              break;
            case ReloadGlobals.TRACEFLAGS.T_FORWARDING:
              this.SelectionColor = Color.DarkCyan;
              break;
            case ReloadGlobals.TRACEFLAGS.T_RELOAD:
              this.SelectionColor = Color.DarkBlue;
              break;
            case ReloadGlobals.TRACEFLAGS.T_USAGE:
              this.SelectionColor = Color.DarkGreen;
              break;
            case ReloadGlobals.TRACEFLAGS.T_WARNING:
              this.SelectionColor = Color.Magenta;
              break;
            case ReloadGlobals.TRACEFLAGS.T_BUG:
              this.SelectionColor = Color.DarkKhaki;
              break;
            case ReloadGlobals.TRACEFLAGS.T_KEEPALIVE:
              this.SelectionColor = Color.DarkGray;
              break;
          }

          this.AppendText(x + "\r\n");

          if (ReloadGlobals.DocumentAutoScroll)
            this.ScrollToCaret();
        }
        else {
          try {
            this.Invoke(new UpdateTextDelegate(WriteLine), new object[] { scope, x });
          }
          catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("WriteLine failed: " + ex.Message);
          }
        }
    }
  }

  public class MDIChild : System.Windows.Forms.Form {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.Container components = null;
    private TabControl tabCtrl;
    private RichTextBoxEx richTextBox1;
    private TabPage tabPag;

    private Machine m_machine;
    private System.Windows.Forms.Timer theTimer = new System.Windows.Forms.Timer();
    private TextWriter measureFile;

    // Declare a delegate that takes a single string parameter

    // Static Function: To which is used in the Delegate. To call the Process()
    // function, we need to declare a logging function: Logger() that matches
    // the signature of the delegate.
    void Logger(ReloadGlobals.TRACEFLAGS scope, string s) {
      try {
        richTextBox1.WriteLine(scope, s);
        LogFileLog(scope, s);
      }
      catch {
      }
    }

    public MDIChild() {
      InitializeComponent();
    }

    public void LogFileLog(ReloadGlobals.TRACEFLAGS traces, string s) {
      string fileName = "log/peer-on-port-" + m_machine.ReloadConfig.ListenPort + ".txt";
      string measureFileName = "log/joining-times.txt";

      if (traces == ReloadGlobals.TRACEFLAGS.T_MEASURE)// || traces == ReloadGlobals.TRACEFLAGS.T_ERROR)
            {
        measureFile = new StreamWriter(measureFileName, true);
        measureFile.WriteLine(tabPag.Text + "\t" + s);
        measureFile.Close();
      }
    }

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    protected override void Dispose(bool disposing) {
      if (disposing) {
        if (components != null) {
          components.Dispose();
        }
      }
      base.Dispose(disposing);
    }

    public TabPage TabPag {
      get {
        return tabPag;
      }
      set {
        tabPag = value;
      }
    }

    public TabControl TabCtrl {
      set {
        tabCtrl = value;
      }
    }

    // Timer raised method
    private void TimerEventProcessor(Object myObject, EventArgs myEventArgs) {
      try {

        if (m_machine.ReloadConfig.LocalNodeID != null) {
          theTimer.Stop();
          this.Text = "NodeID: " + m_machine.ReloadConfig.LocalNodeID.ToString();
        }
      }
      catch { }
    }

    #region Windows Form Designer generated code
    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      this.richTextBox1 = new ReloadMDI.RichTextBoxEx();
      this.SuspendLayout();
      // 
      // richTextBox1
      // 
      this.richTextBox1.Cursor = System.Windows.Forms.Cursors.Default;
      this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.richTextBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      this.richTextBox1.Location = new System.Drawing.Point(0, 0);
      this.richTextBox1.Name = "richTextBox1";
      this.richTextBox1.Size = new System.Drawing.Size(704, 602);
      this.richTextBox1.TabIndex = 0;
      this.richTextBox1.Text = "";

      // 
      // MDIChild
      // 
      this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
      this.BackColor = System.Drawing.SystemColors.InactiveCaptionText;
      this.ClientSize = new System.Drawing.Size(704, 602);
      this.Controls.Add(this.richTextBox1);
      this.Name = "MDIChild";
      this.Text = "RELOAD Node";
      this.Activated += new System.EventHandler(this.MDIChild_Activated);
      this.Closing += new System.ComponentModel.CancelEventHandler(this.MDIChild_Closing);
      this.ResumeLayout(false);

    }
    #endregion

    private void MDIChild_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      m_machine.Finish();

      //Destroy the corresponding Tabpage when closing MDI child form
      this.tabPag.Dispose();

      //If no Tabpage left
      if (!tabCtrl.HasChildren) {
        tabCtrl.Visible = false;
      }
    }

    private void MDIChild_Activated(object sender, System.EventArgs e) {
      //Activate the corresponding Tabpage
      tabCtrl.SelectedTab = tabPag;

      if (!tabCtrl.Visible) {
        tabCtrl.Visible = true;
      }
    }

    internal void StartReloadEngine(int ListenPort) {
      m_machine = new Machine();
      m_machine.ReloadConfig.Logger = new ReloadConfig.LogHandler(Logger);
      m_machine.ReloadConfig.ListenPort = ListenPort;
      m_machine.ReloadConfig.IamClient = ReloadMDI.Properties.Settings.Default.Client;
      m_machine.ReloadConfig.TabPage = this.Text;

      m_machine.StartWorker();

      //Adds the event and the event handler for the method that will 
      //process the timer event to the timer
      theTimer.Tick += new EventHandler(TimerEventProcessor);

      // Sets the timer interval to 5 seconds
      theTimer.Interval = 2000;
      theTimer.Start();

    }

    internal void RELOAD_Cmd_Join() {
      m_machine.SendCommand("PreJoin");
    }

    internal void RELOAD_Cmd_Leave() {
      m_machine.SendCommand("Leave");
    }

    internal void Store(string store) {
      /*
        // TODO alex: adapt GUI handle several Usages            
        if (store == "")
        {
            string forwardURL = String.Format("sip:t-reload-vnode-{0}@{1}", m_machine.ReloadConfig.ListenPort, ReloadGlobals.OverlayName);
            // storage as URI reference
            m_machine.GatherCommands("Store", Usage_Code_Point.SIP_REGISTRATION, 1, forwardURL);
        }
        else
        {                
            // storing destination
            m_machine.GatherCommands("Store", Usage_Code_Point.SIP_REGISTRATION, 2);
        }
        m_machine.SendCommand("Store");
       */
      m_machine.GatherCommands("Store",
            Usage_Code_Point.DISCO, 0, store, "1");
      // TODO should be a foreach, just one element for testing
      m_machine.GatherCommands("Store",
          Usage_Code_Point.ACCESS_LIST, 0, store, "test@rest.org",
          ReloadGlobals.DISCO_REGISTRATION_KIND_ID, true);
      m_machine.SendCommand("Store");
    }

    internal void Fetch(string fetch) {
      // TODO alex: adapt GUI handle several Usages
      // m_machine.SendCommand("Fetch," + fetch); -original

      /*m_machine.GatherCommands("Fetch", Usage_Code_Point.SIP_REGISTRATION, 1, fetch); // type does not mater
      m_machine.SendCommand("Fetch");
      */
      m_machine.GatherCommands("Fetch",
        Usage_Code_Point.DISCO, 0, fetch);
      m_machine.GatherCommands("Fetch",
        Usage_Code_Point.ACCESS_LIST, 0, fetch, "1", "2");
      m_machine.SendCommand("Fetch");
    }

    internal void Remove() {
      m_machine.SendCommand("Remove");
    }

    internal void SetE164(string e164) {
      ReloadConfigResolve res = new ReloadConfigResolve(m_machine.ReloadConfig);
      string sip_uri;
      sip_uri = res.ResolveNaptr(e164);

      if (sip_uri == null) {
        //fallback mechanism of converting e164 number to a sip uri
        Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
          "DNS Enum fallback to sip uri analysis");
        sip_uri = e164.TrimStart(' ');
        sip_uri = e164.Replace(" ", "");
        sip_uri = "sip:" + e164 + "@" + ReloadGlobals.OverlayName;
      }
      m_machine.ReloadConfig.SipUri = sip_uri;
    }

    internal void ClearDocument() {
      richTextBox1.Text = "";
    }

    internal void Info() {
      m_machine.SendCommand("Info");
    }

    internal void Finish() {
      m_machine.Finish();
    }
  }
}
