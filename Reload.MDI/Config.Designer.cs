
namespace ReloadMDI
{
    partial class ConfigForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.textBoxTraceFlags = new System.Windows.Forms.TextBox();
            this.checkBoxReportingIncludesConnections = new System.Windows.Forms.CheckBox();
            this.checkBoxReporting = new System.Windows.Forms.CheckBox();
            this.checkBoxIgnoreSSL_Errors = new System.Windows.Forms.CheckBox();
            this.checkBoxTLS = new System.Windows.Forms.CheckBox();
            this.checkBoxClient = new System.Windows.Forms.CheckBox();
            this.textBoxDNS_Server = new System.Windows.Forms.TextBox();
            this.textBoxListenPort = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.checkBoxForceLocalConfig = new System.Windows.Forms.CheckBox();
            this.checkBoxFraming = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(271, 434);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(75, 23);
            this.buttonOK.TabIndex = 6;
            this.buttonOK.Text = "OK";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Location = new System.Drawing.Point(382, 434);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 23);
            this.buttonCancel.TabIndex = 7;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(43, 380);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(76, 16);
            this.label1.TabIndex = 9;
            this.label1.Text = "Traceflags:";
            // 
            // textBoxTraceFlags
            // 
            this.textBoxTraceFlags.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxTraceFlags.Location = new System.Drawing.Point(125, 377);
            this.textBoxTraceFlags.Name = "textBoxTraceFlags";
            this.textBoxTraceFlags.Size = new System.Drawing.Size(107, 22);
            this.textBoxTraceFlags.TabIndex = 10;
            this.textBoxTraceFlags.Text = "1048351";
            this.textBoxTraceFlags.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // checkBoxReportingIncludesConnections
            // 
            this.checkBoxReportingIncludesConnections.AutoSize = true;
            this.checkBoxReportingIncludesConnections.Checked = true;
            this.checkBoxReportingIncludesConnections.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxReportingIncludesConnections.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxReportingIncludesConnections.Location = new System.Drawing.Point(78, 301);
            this.checkBoxReportingIncludesConnections.Name = "checkBoxReportingIncludesConnections";
            this.checkBoxReportingIncludesConnections.Size = new System.Drawing.Size(214, 20);
            this.checkBoxReportingIncludesConnections.TabIndex = 8;
            this.checkBoxReportingIncludesConnections.Text = "Reporting includes connections";
            this.checkBoxReportingIncludesConnections.UseVisualStyleBackColor = true;
            // 
            // checkBoxReporting
            // 
            this.checkBoxReporting.AutoSize = true;
            this.checkBoxReporting.Checked = true;
            this.checkBoxReporting.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxReporting.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxReporting.Location = new System.Drawing.Point(46, 275);
            this.checkBoxReporting.Name = "checkBoxReporting";
            this.checkBoxReporting.Size = new System.Drawing.Size(86, 20);
            this.checkBoxReporting.TabIndex = 5;
            this.checkBoxReporting.Text = "Reporting";
            this.checkBoxReporting.UseVisualStyleBackColor = true;
            // 
            // checkBoxIgnoreSSL_Errors
            // 
            this.checkBoxIgnoreSSL_Errors.AutoSize = true;
            this.checkBoxIgnoreSSL_Errors.Checked = true;
            this.checkBoxIgnoreSSL_Errors.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxIgnoreSSL_Errors.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxIgnoreSSL_Errors.Location = new System.Drawing.Point(46, 233);
            this.checkBoxIgnoreSSL_Errors.Name = "checkBoxIgnoreSSL_Errors";
            this.checkBoxIgnoreSSL_Errors.Size = new System.Drawing.Size(131, 20);
            this.checkBoxIgnoreSSL_Errors.TabIndex = 3;
            this.checkBoxIgnoreSSL_Errors.Text = "Ignore SSL errors";
            this.checkBoxIgnoreSSL_Errors.UseVisualStyleBackColor = true;
            // 
            // checkBoxTLS
            // 
            this.checkBoxTLS.AutoSize = true;
            this.checkBoxTLS.Checked = true;
            this.checkBoxTLS.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxTLS.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxTLS.Location = new System.Drawing.Point(46, 156);
            this.checkBoxTLS.Name = "checkBoxTLS";
            this.checkBoxTLS.Size = new System.Drawing.Size(209, 20);
            this.checkBoxTLS.TabIndex = 1;
            this.checkBoxTLS.Text = "TLS (Transport Layer Security)";
            this.checkBoxTLS.UseVisualStyleBackColor = true;
            // 
            // checkBoxClient
            // 
            this.checkBoxClient.AutoSize = true;
            this.checkBoxClient.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxClient.Location = new System.Drawing.Point(46, 121);
            this.checkBoxClient.Name = "checkBoxClient";
            this.checkBoxClient.Size = new System.Drawing.Size(108, 20);
            this.checkBoxClient.TabIndex = 0;
            this.checkBoxClient.Text = "Stay as Client";
            this.checkBoxClient.UseVisualStyleBackColor = true;
            // 
            // textBoxDNS_Server
            // 
            this.textBoxDNS_Server.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::ReloadMDI.Properties.Settings.Default, "DNS_Address", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.textBoxDNS_Server.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxDNS_Server.Location = new System.Drawing.Point(202, 44);
            this.textBoxDNS_Server.Name = "textBoxDNS_Server";
            this.textBoxDNS_Server.Size = new System.Drawing.Size(132, 22);
            this.textBoxDNS_Server.TabIndex = 13;
            this.textBoxDNS_Server.Text = global::ReloadMDI.Properties.Settings.Default.DNS_Address;
            this.textBoxDNS_Server.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // textBoxListenPort
            // 
            this.textBoxListenPort.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxListenPort.Location = new System.Drawing.Point(251, 79);
            this.textBoxListenPort.Name = "textBoxListenPort";
            this.textBoxListenPort.Size = new System.Drawing.Size(81, 22);
            this.textBoxListenPort.TabIndex = 14;
            this.textBoxListenPort.Text = "9001";
            this.textBoxListenPort.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(43, 47);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(83, 16);
            this.label3.TabIndex = 11;
            this.label3.Text = "DNS Server:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(43, 82);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(129, 16);
            this.label2.TabIndex = 12;
            this.label2.Text = "Listenport starts with:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
            this.label4.Location = new System.Drawing.Point(183, 234);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(257, 16);
            this.label4.TabIndex = 11;
            this.label4.Text = " (as long as server has no valid certificate)";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label6.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
            this.label6.Location = new System.Drawing.Point(157, 122);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(117, 16);
            this.label6.TabIndex = 11;
            this.label6.Text = "(don\'t join overlay)";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label7.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
            this.label7.Location = new System.Drawing.Point(298, 302);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(171, 16);
            this.label7.TabIndex = 11;
            this.label7.Text = "(slows down reporting view)";
            // 
            // checkBoxForceLocalConfig
            // 
            this.checkBoxForceLocalConfig.AutoSize = true;
            this.checkBoxForceLocalConfig.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxForceLocalConfig.Location = new System.Drawing.Point(46, 338);
            this.checkBoxForceLocalConfig.Name = "checkBoxForceLocalConfig";
            this.checkBoxForceLocalConfig.Size = new System.Drawing.Size(218, 20);
            this.checkBoxForceLocalConfig.TabIndex = 5;
            this.checkBoxForceLocalConfig.Text = "Force use of local enrollment file";
            this.checkBoxForceLocalConfig.UseVisualStyleBackColor = true;
            // 
            // checkBoxFraming
            // 
            this.checkBoxFraming.AutoSize = true;
            this.checkBoxFraming.Checked = true;
            this.checkBoxFraming.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxFraming.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBoxFraming.Location = new System.Drawing.Point(46, 193);
            this.checkBoxFraming.Name = "checkBoxFraming";
            this.checkBoxFraming.Size = new System.Drawing.Size(76, 20);
            this.checkBoxFraming.TabIndex = 15;
            this.checkBoxFraming.Text = "Framing";
            this.checkBoxFraming.UseVisualStyleBackColor = true;
            // 
            // ConfigForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(488, 490);
            this.Controls.Add(this.checkBoxFraming);
            this.Controls.Add(this.textBoxDNS_Server);
            this.Controls.Add(this.textBoxListenPort);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textBoxTraceFlags);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.checkBoxReportingIncludesConnections);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.checkBoxForceLocalConfig);
            this.Controls.Add(this.checkBoxReporting);
            this.Controls.Add(this.checkBoxIgnoreSSL_Errors);
            this.Controls.Add(this.checkBoxTLS);
            this.Controls.Add(this.checkBoxClient);
            this.Name = "ConfigForm";
            this.Text = "Configuration Settings";
            this.Load += new System.EventHandler(this.ConfigForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox checkBoxClient;
        private System.Windows.Forms.CheckBox checkBoxTLS;
        private System.Windows.Forms.CheckBox checkBoxIgnoreSSL_Errors;
        private System.Windows.Forms.CheckBox checkBoxReporting;
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.CheckBox checkBoxReportingIncludesConnections;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBoxTraceFlags;
        private System.Windows.Forms.TextBox textBoxDNS_Server;
        private System.Windows.Forms.TextBox textBoxListenPort;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.CheckBox checkBoxForceLocalConfig;
        private System.Windows.Forms.CheckBox checkBoxFraming;
    }
}