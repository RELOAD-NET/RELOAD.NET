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
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Topology;

namespace TSystems.RELOAD.Extension {
  public class GateWay {
    public GWMachine mainPeer;
    public GWMachine interDomainPeer;

    private ReDiR ReDiRMainNode;
    private ReDiR ReDiRinterDomainNode;

    private GatewayRequestHandler gwRequestHandler;
    
    //maps TransactionId to ResourceId needed for signature verification!
    //TransactionId's are recorded for every FetchRequest
    private Dictionary<ulong, ResourceId> fetchIdMap = null;


    public delegate void DInterdomainMessageProcessed(string destination_overlay, bool destination_reached);
    public event DInterdomainMessageProcessed InterdomainMessageProcessed;

    //private Dictionary<string, ReloadMessage> forwardBuffer = new Dictionary<string,ReloadMessage>();
    //Buffer for Messages which needs to be forwarded but the responsible GateWay is unknown
    private Dictionary<string, Queue<ReloadMessage>> forwardBuffer = new Dictionary<string, Queue<ReloadMessage>>();

    void machineMain_StateUpdate(ReloadConfig.RELOAD_State state) {

      if (state == ReloadConfig.RELOAD_State.Configured && mainPeer.ReloadConfig.IsBootstrap == true)
        ReDiRMainNode.registerService("GATEWAYNODE");

      if (state == ReloadConfig.RELOAD_State.Joined)
        ReDiRMainNode.registerService("GATEWAYNODE");

    }

    void machineIntraDomain_StateUpdate(ReloadConfig.RELOAD_State state) {

      if (state == ReloadConfig.RELOAD_State.Configured && mainPeer.ReloadConfig.IsBootstrap == true)
        ReDiRinterDomainNode.registerService(mainPeer.ReloadConfig.OverlayName);

      if (state == ReloadConfig.RELOAD_State.Joined)
        ReDiRinterDomainNode.registerService(mainPeer.ReloadConfig.OverlayName);

    }

    public GateWay(GWMachine mainPeer, GWMachine interDomainPeer) {

      fetchIdMap = new Dictionary<ulong, ResourceId>();

      gwRequestHandler = new GatewayRequestHandler(interDomainPeer);

      this.mainPeer = mainPeer;
      mainPeer.GateWay = this;

      ReDiRMainNode = new ReDiR(mainPeer);

      mainPeer.StateUpdates += machineMain_StateUpdate;

      this.interDomainPeer = interDomainPeer;
      interDomainPeer.GateWay = this;

      ReDiRinterDomainNode = new ReDiR(interDomainPeer);

      interDomainPeer.StateUpdates += machineIntraDomain_StateUpdate;
    }

    bool redirIntraDomain_LookupFailed(ResourceId resid) {

      interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "interDomainPeer redir_LookupFailed: ResourceId: " + resid);

      return true;
    }

    bool ReDiRMainNode_LookupFailed(ResourceId resid) {

      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "mainPeer redir_LookupFailed: ResourceId: " + resid);

      return true;
    }

    public void start() {
      mainPeer.StartWorker();
      interDomainPeer.StartWorker();
    }

    public void response_processed(bool destination_reached) {

      if (InterdomainMessageProcessed != null)
        InterdomainMessageProcessed("RESPONSE", destination_reached);

    }

    public void Receive(string destination_overlay, ReloadMessage reloadMsg) {

      if ((reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Fetch_Request)) {
        FetchReq req = (FetchReq)reloadMsg.reload_message_body;

        fetchIdMap[reloadMsg.TransactionID] = req.ResourceId;
        mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: Fetch_Request: " + reloadMsg.TransactionID + " ResId: " + req.ResourceId);
      }

      if (mainPeer.ReloadConfig.OverlayName == destination_overlay) {

        interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("Message received by GateWayPeer"));
        mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GATEWAY: RECEIVE");
        if (reloadMsg.forwarding_header.via_list == null)
          reloadMsg.forwarding_header.via_list = new List<Destination>();
        //reloadMsg.AddViaHeader(interDomainPeer.Topology.LocalNode.Id);

        //---------------DEBUG
        mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("via_list"));
        if (reloadMsg.forwarding_header.via_list != null) {
          foreach (Destination destx in reloadMsg.forwarding_header.via_list)
            mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("    Via={0} ", destx.ToString()));
        }
        mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("destination_list"));
        if (reloadMsg.forwarding_header.destination_list != null) {
          foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
            mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("    Dest={0} ", desty.ToString()));
        }
        //---------------DEBUG
        mainPeer.Inject(reloadMsg);
        if (InterdomainMessageProcessed != null)
          InterdomainMessageProcessed(destination_overlay, true);

      }
      else {

        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("Message: source_overlay " + source_overlay + " destination_overlay " + destination_overlay));
        interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GATEWAY: SEND into " + destination_overlay);
        if (reloadMsg.forwarding_header.via_list == null)
          reloadMsg.forwarding_header.via_list = new List<Destination>();
        //reloadMsg.AddViaHeader(mainPeer.Topology.LocalNode.Id);

        //---------------DEBUG
        interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("via_list"));
        if (reloadMsg.forwarding_header.via_list != null) {
          foreach (Destination destx in reloadMsg.forwarding_header.via_list)
            interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("    Via={0} ", destx.ToString()));
        }
        interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("destination_list"));
        if (reloadMsg.forwarding_header.destination_list != null) {
          foreach (Destination desty in reloadMsg.forwarding_header.destination_list)
            interDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, String.Format("    Dest={0} ", desty.ToString()));
        }
        //---------------DEBUG

        //reloadMsg.LastHopNodeId = interDomainPeer.Topology.Id;  //Why

        gwRequestHandler.forwardVia(destination_overlay, reloadMsg);
        if (InterdomainMessageProcessed != null)
          InterdomainMessageProcessed(destination_overlay, false);
      }
    }

    public ResourceId getResIdforTransactionId(ulong TransactionId) {
      ResourceId res_id = fetchIdMap[TransactionId];
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: get: " + TransactionId);
      return res_id;
    }

    public void removeResIdforTransactionId(ulong TransactionId) {
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "GW: remove: " + TransactionId);
      fetchIdMap.Remove(TransactionId);
    }

  }
}
