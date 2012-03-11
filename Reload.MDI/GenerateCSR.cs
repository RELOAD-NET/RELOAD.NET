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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TSystems.RELOAD;
using TSystems.RELOAD.Enroll;

namespace ReloadMDI
{
    public partial class GCSR : Form
    {
        private ReloadConfig reloadConfig = null;

        public GCSR()
        {
            InitializeComponent();

            reloadConfig = new ReloadConfig();
            reloadConfig.Logger = new ReloadConfig.LogHandler(Logger);
        }

        // Static Function: To which is used in the Delegate. To call the Process()
        // function, we need to declare a logging function: Logger() that matches
        // the signature of the delegate.
        void Logger(ReloadGlobals.TRACEFLAGS scope, string s)
        {
            richTextBox1.WriteLine(scope, s);
        }


        private void OK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void buttonGenerate_Click(object sender, EventArgs e)
        {
            reloadConfig.IMSI = textIMSI.Text;
            ReloadConfigResolve resolve = new ReloadConfigResolve(reloadConfig);

            resolve.CertificateSigningRequest(null);
        }
     }
}
