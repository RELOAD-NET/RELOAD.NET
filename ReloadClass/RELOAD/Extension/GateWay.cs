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
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Topology;

namespace TSystems.RELOAD.Extension {
  public class GateWay {
    public GWMachine mainPeer;
    public GWMachine intraDomainPeer;

    private ReDiR redirMainNode;
    private ReDiR redirintraDomainNode;

    private GatewayRequestHandler gwRequestHandler;

    private System.Timers.Timer joinedTimer;

    //private Dictionary<string, ReloadMessage> forwardBuffer = new Dictionary<string,ReloadMessage>();
    //Buffer for Messages which needs to be forwarded but the responsible GateWay is unknown
    private Dictionary<string, Queue<ReloadMessage>> forwardBuffer = new Dictionary<string, Queue<ReloadMessage>>();

    void machineMain_StateUpdate(ReloadConfig.RELOAD_State state) {

      if (state == ReloadConfig.RELOAD_State.Configured && mainPeer.ReloadConfig.IsBootstrap == true)
        redirMainNode.registerService("GATEWAYNODE");

      if (state == ReloadConfig.RELOAD_State.Joined)
        redirMainNode.registerService("GATEWAYNODE");

    }

    void machineIntraDomain_StateUpdate(ReloadConfig.RELOAD_State state) {

      if (state == ReloadConfig.RELOAD_State.Configured && mainPeer.ReloadConfig.IsBootstrap == true)
        redirintraDomainNode.registerService(mainPeer.ReloadConfig.OverlayName);

      if (state == ReloadConfig.RELOAD_State.Joined)
        redirintraDomainNode.registerService(mainPeer.ReloadConfig.OverlayName);

    }

    public GateWay(GWMachine mainPeer, GWMachine intraDomainPeer) {
      gwRequestHandler = new GatewayRequestHandler(intraDomainPeer);

      this.mainPeer = mainPeer;
      mainPeer.GateWay = this;

      redirMainNode = new ReDiR(mainPeer);
      //redirMainNode.ReDiRLookupCompleted += redir_LookupCompleted;
      //redirMainNode.ReDiRLookupFailed += redirMainNode_LookupFailed;
      mainPeer.StateUpdates += machineMain_StateUpdate;
      
      this.intraDomainPeer = intraDomainPeer;
      intraDomainPeer.GateWay = this;

      redirintraDomainNode = new ReDiR(intraDomainPeer);
      //redirintraDomainNode.ReDiRLookupCompleted += redir_LookupCompleted;
      //redirintraDomainNode.ReDiRLookupFailed += redirIntraDomain_LookupFailed;
      intraDomainPeer.StateUpdates += machineIntraDomain_StateUpdate;
    }

    bool redirIntraDomain_LookupFailed(ResourceId resid) {

      intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "intraDomainPeer redir_LookupFailed: ResourceId: " + resid);

      return true;
    }

    bool redirMainNode_LookupFailed(ResourceId resid) {

      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "mainPeer redir_LookupFailed: ResourceId: " + resid);

      return true;
    }

    //TODO: delete no longer used
    private bool redir_LookupCompleted(string nameSpace, NodeId id) //use nameSpace to verify callback TODO:
    {

      intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "intraDomainPeer Ausgang gefunden: " + id.ToString());

      ReloadMessage message = null;

      if (!forwardBuffer.ContainsKey(nameSpace)) {
        throw new System.Exception(String.Format("ReDiR Result for {0} but no Request! This should not happen!", nameSpace));
      }
      else
        message = forwardBuffer[nameSpace].Dequeue();

      Node NextHopNode = intraDomainPeer.Topology.routing_table.FindNextHopTo(id, true, false);       //TODO: (id, true, true)?

      if (message.forwarding_header.destination_list[0].type == DestinationType.node &&
          message.forwarding_header.destination_list[0].destination_data.node_id == mainPeer.Topology.LocalNode.Id) //TODO immer ressource id oder auch manchmal node id?
        message.forwarding_header.destination_list[0] = new Destination(id);
      else if (message.forwarding_header.destination_list[0].type == DestinationType.resource &&
          message.forwarding_header.destination_list[0].destination_data.ressource_id == mainPeer.Topology.LocalNode.Id)
        message.forwarding_header.destination_list[0] = new Destination(id);
      else
        mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

      //message.forwarding_header.destination_list.Insert(0,new Destination(id)); 
      //---------------DEBUG
      if (message.forwarding_header.via_list != null) {
        foreach (Destination destx in message.forwarding_header.via_list)
          intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
      }
      if (message.forwarding_header.destination_list != null) {
        foreach (Destination desty in message.forwarding_header.destination_list)
          if (desty.type != DestinationType.node || message.forwarding_header.destination_list.Count > 1)
            intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
      }
      //---------------DEBUG

      //set the LastHopNodeId to the NodeId of the intraDomainPeer
      message.LastHopNodeId = intraDomainPeer.Topology.Id;
      //and send...
      intraDomainPeer.Transport.send(message, NextHopNode);

      return true;
    }

    public void start() {
      mainPeer.StartWorker();
      intraDomainPeer.StartWorker();
    }

    public void Receive(string source_overlay, ReloadMessage message) {
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GATEWAY: RECEIVE im ZIELOVERLAY");

      //---------------DEBUG
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("via_list before"));
      if (message.forwarding_header.via_list != null) {
        foreach (Destination destx in message.forwarding_header.via_list)
          mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
      }
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("destination_list before"));
      if (message.forwarding_header.destination_list != null) {
        foreach (Destination desty in message.forwarding_header.destination_list)
          mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
      }
      //---------------DEBUG

      if (message.forwarding_header.destination_list[0].type == DestinationType.node &&
          message.forwarding_header.destination_list[0].destination_data.node_id == intraDomainPeer.Topology.LocalNode.Id)
        message.forwarding_header.destination_list[0] = new Destination(mainPeer.Topology.LocalNode.Id);
      else {

      }
      
      //---------------DEBUG
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("via_list after"));
      if (message.forwarding_header.via_list != null) {
        foreach (Destination destx in message.forwarding_header.via_list)
          mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
      }
      mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("destination_list after"));
      if (message.forwarding_header.destination_list != null) {
        foreach (Destination desty in message.forwarding_header.destination_list)
            mainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
      }
      //---------------DEBUG

      mainPeer.Inject(source_overlay, message);
    }

    public void Send(string from_overlay, string to_overlay, ReloadMessage message) {

      intraDomainPeer.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "SUCHE ZIEL " + to_overlay + " IM INTRADOMAIN OVERLAY");
      //Request from mainOverlay in interdomainOverlay: FirstDestEntry is MainPeerNodeId
    //  if (message.IsRequest()) {
    //    if (message.forwarding_header.destination_list[0].type == DestinationType.node &&
    //message.forwarding_header.destination_list[0].destination_data.node_id != mainPeer.Topology.LocalNode.Id) {

    //    }
    //    message.RemoveFirstDestEntry();

    //  }
    //  else if (message.forwarding_header.destination_list[0].type == DestinationType.node &&
    //message.forwarding_header.destination_list[0].destination_data.node_id != mainPeer.Topology.LocalNode.Id) {

    //  }
    //  else 
        if (message.forwarding_header.destination_list[0].type == DestinationType.node &&
            message.forwarding_header.destination_list[0].destination_data.node_id == mainPeer.Topology.LocalNode.Id) {
              message.RemoveFirstDestEntry();
        }
      
      gwRequestHandler.forward(from_overlay, to_overlay, message);

    }
  }
}
