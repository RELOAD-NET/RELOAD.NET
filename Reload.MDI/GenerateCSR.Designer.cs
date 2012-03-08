namespace ReloadMDI
{
    partial class GCSR
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
            this.label1 = new System.Windows.Forms.Label();
            this.textIMSI = new System.Windows.Forms.TextBox();
            this.OK = new System.Windows.Forms.Button();
            this.richTextBox1 = new ReloadMDI.RichTextBoxEx();
            this.tElMessageEncryptor1 = new SBMessages.TElMessageEncryptor();
            this.buttonGenerate = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(31, 39);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Enter IMSI:";
            // 
            // textIMSI
            // 
            this.textIMSI.ForeColor = System.Drawing.SystemColors.InfoText;
            this.textIMSI.Location = new System.Drawing.Point(47, 55);
            this.textIMSI.Name = "textIMSI";
            this.textIMSI.Size = new System.Drawing.Size(260, 20);
            this.textIMSI.TabIndex = 1;
            this.textIMSI.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // OK
            // 
            this.OK.Location = new System.Drawing.Point(403, 320);
            this.OK.Name = "OK";
            this.OK.Size = new System.Drawing.Size(75, 23);
            this.OK.TabIndex = 2;
            this.OK.Text = "OK";
            this.OK.UseVisualStyleBackColor = true;
            this.OK.Click += new System.EventHandler(this.OK_Click);
            // 
            // richTextBox1
            // 
            this.richTextBox1.Cursor = System.Windows.Forms.Cursors.Default;
            this.richTextBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.richTextBox1.Location = new System.Drawing.Point(34, 96);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(444, 178);
            this.richTextBox1.TabIndex = 3;
            this.richTextBox1.Text = "";
            // 
            // tElMessageEncryptor1
            // 
            this.tElMessageEncryptor1.Algorithm = 28675;
            this.tElMessageEncryptor1.BitsInKey = 0;
            this.tElMessageEncryptor1.CertStorage = null;
            this.tElMessageEncryptor1.CryptoProviderManager = null;
            this.tElMessageEncryptor1.EncryptionOptions = ((short)(1));
            this.tElMessageEncryptor1.OriginatorCertificates = null;
            this.tElMessageEncryptor1.OriginatorCRLs = null;
            this.tElMessageEncryptor1.Tag = null;
            this.tElMessageEncryptor1.UseImplicitContentEncoding = false;
            this.tElMessageEncryptor1.UseOAEP = false;
            this.tElMessageEncryptor1.UseUndefSize = true;
            // 
            // buttonGenerate
            // 
            this.buttonGenerate.Location = new System.Drawing.Point(403, 55);
            this.buttonGenerate.Name = "buttonGenerate";
            this.buttonGenerate.Size = new System.Drawing.Size(75, 23);
            this.buttonGenerate.TabIndex = 4;
            this.buttonGenerate.Text = "Generate";
            this.buttonGenerate.UseVisualStyleBackColor = true;
            this.buttonGenerate.Click += new System.EventHandler(this.buttonGenerate_Click);
            // 
            // GCSR
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(509, 355);
            this.Controls.Add(this.buttonGenerate);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.OK);
            this.Controls.Add(this.textIMSI);
            this.Controls.Add(this.label1);
            this.Name = "GCSR";
            this.Text = "Generate Certificate Signing Request";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textIMSI;
        private System.Windows.Forms.Button OK;
        private RichTextBoxEx richTextBox1;
        private SBMessages.TElMessageEncryptor tElMessageEncryptor1;
        private System.Windows.Forms.Button buttonGenerate;
    }
}