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
namespace PocketDnDnsLookup
{
    partial class PocketDnDnsLookup
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.MainMenu mainMenu1;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.mainMenu1 = new System.Windows.Forms.MainMenu();
            this.txtNameToLookup = new System.Windows.Forms.TextBox();
            this.btnLookup = new System.Windows.Forms.Button();
            this.lstBxQueryType = new System.Windows.Forms.ListBox();
            this.lblLookupName = new System.Windows.Forms.Label();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.txtDnsServer = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // txtNameToLookup
            // 
            this.txtNameToLookup.Location = new System.Drawing.Point(79, 39);
            this.txtNameToLookup.Name = "txtNameToLookup";
            this.txtNameToLookup.Size = new System.Drawing.Size(158, 21);
            this.txtNameToLookup.TabIndex = 1;
            // 
            // btnLookup
            // 
            this.btnLookup.Location = new System.Drawing.Point(79, 161);
            this.btnLookup.Name = "btnLookup";
            this.btnLookup.Size = new System.Drawing.Size(72, 20);
            this.btnLookup.TabIndex = 3;
            this.btnLookup.Text = "Lookup";
            this.btnLookup.Click += new System.EventHandler(this.btnLookup_Click);
            // 
            // lstBxQueryType
            // 
            this.lstBxQueryType.Location = new System.Drawing.Point(79, 69);
            this.lstBxQueryType.Name = "lstBxQueryType";
            this.lstBxQueryType.Size = new System.Drawing.Size(158, 86);
            this.lstBxQueryType.TabIndex = 2;
            // 
            // lblLookupName
            // 
            this.lblLookupName.Location = new System.Drawing.Point(4, 39);
            this.lblLookupName.Name = "lblLookupName";
            this.lblLookupName.Size = new System.Drawing.Size(69, 20);
            this.lblLookupName.Text = "Lookup:";
            // 
            // txtOutput
            // 
            this.txtOutput.Location = new System.Drawing.Point(4, 189);
            this.txtOutput.Multiline = true;
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtOutput.Size = new System.Drawing.Size(233, 76);
            this.txtOutput.TabIndex = 4;
            // 
            // txtDnsServer
            // 
            this.txtDnsServer.Location = new System.Drawing.Point(79, 11);
            this.txtDnsServer.Name = "txtDnsServer";
            this.txtDnsServer.Size = new System.Drawing.Size(158, 21);
            this.txtDnsServer.TabIndex = 0;
            this.txtDnsServer.Text = "208.67.222.222";
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(4, 11);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(77, 20);
            this.label1.Text = "DNS Server:";
            // 
            // PocketDnDnsLookup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoScroll = true;
            this.ClientSize = new System.Drawing.Size(240, 268);
            this.Controls.Add(this.txtDnsServer);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtOutput);
            this.Controls.Add(this.txtNameToLookup);
            this.Controls.Add(this.lblLookupName);
            this.Controls.Add(this.lstBxQueryType);
            this.Controls.Add(this.btnLookup);
            this.Menu = this.mainMenu1;
            this.Name = "PocketDnDnsLookup";
            this.Text = "Pocket DnDns Lookup";
            this.Load += new System.EventHandler(this.PocketDnDnsLookup_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TextBox txtNameToLookup;
        private System.Windows.Forms.Button btnLookup;
        private System.Windows.Forms.ListBox lstBxQueryType;
        private System.Windows.Forms.Label lblLookupName;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.TextBox txtDnsServer;
        private System.Windows.Forms.Label label1;
    }
}

