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
using System.Linq;
using System.Text;
using System.ComponentModel;
using TSystems.RELOAD.Transport;
using System.IO;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Utils;
using SBX509;
using SBPublicKeyCrypto;

namespace TSystems.RELOAD.Extension {
  public class GWMachine : Machine {
    //reference for GateWay instance: connects to GWMachine Instances
    private GateWay m_GateWay = null;
    //maps TransactionID to requested ResourceID needed for signature verification!
    private Dictionary<ulong, ResourceId> FetchIdMap = null;

    public GateWay GateWay {
      get { return m_GateWay; }
      set { m_GateWay = value; }
    }

    public GWMachine()
      : base() {
      FetchIdMap = new Dictionary<ulong, ResourceId>();
    }

    internal void Inject(ReloadMessage reloadMsg) {

      if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Fetch_Answer) {  //resign StoredData

        ResourceId res_id = GateWay.getResIdforTransactionId(reloadMsg.TransactionID);
        GateWay.removeResIdforTransactionId(reloadMsg.TransactionID);

        FetchAns answ = (FetchAns)reloadMsg.reload_message_body;
        List<FetchKindResponse> fetchKindResponses = answ.KindResponses;
        foreach (FetchKindResponse kind in fetchKindResponses) {
          foreach (StoredData sd in kind.values) {
            sd.SignData(res_id, kind.kind, ReloadConfig.AccessController.MyIdentity, ReloadConfig);
            ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: DATA RESIGNED!");
          }
        }
      }

      reloadMsg.security_block = new SecurityBlock(ReloadConfig, ReloadConfig.AccessController.MyIdentity);
      reloadMsg.security_block.SignMessage(ReloadConfig.OverlayHash, //TODO: remove overlayhash from glals
       reloadMsg.TransactionID.ToString(), reloadMsg.reload_message_body);
      ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: " + reloadMsg.reload_message_body.RELOAD_MsgCode.ToString() + "Message resigned (new SecurityBlock)");

      Transport.receive_message(reloadMsg);

    }

    //verifies message signature and StoredData Signatures and authenticates the certificates used to sign the signatures
    internal bool Validate(ReloadMessage reloadMsg) {
      bool requestPermitted = ReloadConfig.AccessController.RequestPermitted(reloadMsg);
      if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Fetch_Answer) {  //verify StoredData signature
        ResourceId res_id = GateWay.getResIdforTransactionId(reloadMsg.TransactionID);

        FetchAns answ = (FetchAns)reloadMsg.reload_message_body;
        if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Fetch_Answer) {
          List<FetchKindResponse> fetchKindResponses = answ.KindResponses;
          foreach (FetchKindResponse kind in fetchKindResponses) {
            foreach (StoredData sd in kind.values) {
              if (!ReloadConfig.AccessController.validateDataSignature(res_id, kind.kind, sd)) {
                kind.values.Remove(sd);
                ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: DATA SIGNATURE INVALID!! => dropped");
              }
              else
                ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: INCOMING DATA SIGNATURE VALID!!");
            }
          }
        }
      }
      if (requestPermitted) {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: INCOMING MESSAGE VERIFIED");
        return true;
      }
      else {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: INCOMING MESSAGE NOT VERIFIED!!!!!!!!!!!!!!!!");
        return false;
      }
    }
  }
}
