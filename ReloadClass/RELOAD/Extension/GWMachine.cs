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
using System.Linq;
using System.Text;
using System.ComponentModel;
using TSystems.RELOAD.Transport;

namespace TSystems.RELOAD.Extension
{
    public class GWMachine : Machine
    {
        //reference for GateWay instance: connects to GWMachine Instances
        private GateWay m_GateWay = null;

        public GateWay GateWay
        {
            get { return m_GateWay; }
            set { m_GateWay = value; }
        }

        public GWMachine() : base()
        {

        }

        public void Inject(string from_overlay, ReloadMessage message)
        {

            //message.security_block.OriginatorNodeID = ReloadConfig.LocalNodeID;

            //if (ReloadConfig.MyCertificate != null)
            //    message.security_block.Cert = ReloadConfig.MyCertificate.CertificateBinary;

            //re-sign for new Overlay
            //....

            //forward to Topology plugin
            //if (false == Forwarding.ProcessMsg(message))
            {
                Transport.receive_message(message);
            }




        }
        //protected override void Completed(object sender, RunWorkerCompletedEventArgs e) //TODO: wieder löschen??
        //{
            //string forwardURL = "sip:*@" + ReloadConfig.OverlayName;
            //// storage as URI reference
            //GatherCommands("Store", Usage_Code_Point.SIP_REGISTRATION, 1, forwardURL);

            //SendCommand("Store");
        //}    
      

    }
}
