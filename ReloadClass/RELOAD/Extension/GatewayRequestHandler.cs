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
using TSystems.RELOAD;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Topology;

namespace TSystems.RELOAD.Extension {
  public class GatewayRequestHandler {

    Machine machine = null;
    ReDiR ReDiRNode = null;
    Dictionary<String, NodeId> gateWayCache = null;  //TODO: implement expiration time



    RequestList RequestQueue = null;

    class RequestList {

      private Dictionary<String, Queue<Request>> RequestQueue = null;

      public RequestList() {
        RequestQueue = new Dictionary<string, Queue<Request>>();

      }

      public Queue<Request> this[string key] {
        get {
          if (RequestQueue.ContainsKey(key) == false)
            RequestQueue[key] = new Queue<Request>();
          return RequestQueue[key];
        }
      }
    }

    public abstract class Request {

      public string[] args;
      public Usage_Code_Point CodePoint;
      protected Machine machine;

      public string DestinationOverlay = null;

      public Request(Machine machine, string resourcename=null, Usage_Code_Point codePoint=0) {
        this.args = new string[] { resourcename };
        this.CodePoint = codePoint;
        this.machine = machine;
      }

      public abstract void fire(string nameSpace, NodeId id);

    }

    public class FetchRequest : Request {

      public FetchRequest(Machine machine, string resourcename, Usage_Code_Point codePoint)
        : base(machine, resourcename, codePoint) {
      }

      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: Fetch via " + nameSpace);

        machine.GatherCommandsInQueue("Fetch", this.CodePoint, 0, id, true, this.args);
        machine.SendCommand("Fetch");
      }
    }

    public class StoreRequest : Request {

      public StoreRequest(Machine machine, string resourcename, Usage_Code_Point codePoint)
        : base(machine, resourcename, codePoint) {
      }

      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: Store via " + nameSpace);

        machine.GatherCommandsInQueue("Store", this.CodePoint, 0, id, true, this.args);
        machine.SendCommand("Store");
      }
    }

    public class ForwardRequest : Request {

      ReloadMessage msg;

      public ForwardRequest(Machine machine, ReloadMessage msg)
        : base(machine) {
          this.msg = msg;
      }

      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: Forward via Gateway " + id + " into " + nameSpace);

        Node NextHopNode = machine.Topology.routing_table.FindNextHopTo(id, true, false);       //TODO: (id, true, true)?
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("1 GatewayRequestHandler: fire: via_list"));
        if (msg.forwarding_header.via_list != null) {
          foreach (Destination destx in msg.forwarding_header.via_list)
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
        }
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("1 GatewayRequestHandler: fire: destination_list"));
        if (msg.forwarding_header.destination_list != null) {
          foreach (Destination desty in msg.forwarding_header.destination_list)
              machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
        }
        if (msg.forwarding_header.via_list == null)
          msg.forwarding_header.via_list = new List<Destination>();
        msg.AddViaHeader(this.machine.Topology.LocalNode.Id);
        //if (msg.forwarding_header.destination_list[0].type == DestinationType.node && msg.forwarding_header.destination_list[0].destination_data.node_id == machine.Topology.LocalNode.Id) { //TODO immer ressource id oder auch manchmal node id?
        //if (machine is GWMachine)
        //  ((GWMachine)machine).GateWay.mainPeer.ReloadConfig.OverlayName == msg.forwarding_header.fw_options.tr
        //msg.forwarding_header.destination_list[0] = new Destination(id);
        //  machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("AUS HOMEOVERLAY nach INTERDOMAIN!"));
        //}
        //else {
        //  machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("????????????????????????????"));
       // }
        //msg.forwarding_header.destination_list.Insert(0,new Destination(id));
        //else if (msg.forwarding_header.destination_list[0].type == DestinationType.resource && msg.forwarding_header.destination_list[0].destination_data.ressource_id == machine.Topology.LocalNode.Id)
        //  msg.forwarding_header.destination_list[0] = new Destination(id);
        //else
        //  machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

        msg.forwarding_header.destination_list.Insert(0, new Destination(id)); 
        //---------------DEBUG
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("2 GatewayRequestHandler: fire: via_list"));
        if (msg.forwarding_header.via_list != null) {
          foreach (Destination destx in msg.forwarding_header.via_list)
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Via={0} ", destx.ToString()));
        }
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("2 GatewayRequestHandler: fire: destination_list"));
        if (msg.forwarding_header.destination_list != null) {
          foreach (Destination desty in msg.forwarding_header.destination_list)
              machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("    Dest={0} ", desty.ToString()));
        }
        //---------------DEBUG

        //set the LastHopNodeId to the NodeId of the intraDomainPeer
        msg.LastHopNodeId = machine.Topology.Id;
        //and send...
        machine.Transport.send(msg, NextHopNode);
      }
    }


    public GatewayRequestHandler(Machine machine) {
      this.machine = machine;
      ReDiRNode = new ReDiR(machine);
      RequestQueue = new RequestList();
      ReDiRNode.ReDiRLookupCompleted += redir_LookupCompleted;
      ReDiRNode.ReDiRLookupFailed += redir_LookupFailed;
      gateWayCache = new Dictionary<string, NodeId>();
    }


    bool redir_LookupCompleted(string nameSpace, NodeId id) {

      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_LookupCompleted: " + " nameSpace: " + nameSpace + " NodeId: " + id);

      while (RequestQueue[nameSpace].Count > 0) {
        Request req = RequestQueue[nameSpace].Dequeue();
        req.fire(nameSpace, id);
      }
      gateWayCache[nameSpace] = id;

      return true;
    }

    bool redir_LookupFailed(ResourceId resid) {

      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_LookupFailed: ResourceId: " + resid);

      return true;
    }

    public void storeVia(string NameSpace, string resourcename, Usage_Code_Point codePoint) {

      StoreRequest req = new StoreRequest(machine, resourcename, codePoint);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace, gateWayCache[NameSpace]);
      }
      else {
        RequestQueue[NameSpace].Enqueue(req);
        ReDiRNode.lookupService(NameSpace);
      }
    }

    public void forward(string from_overlay, string to_overlay, ReloadMessage message) {

      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: going to forward form " + from_overlay + " into " + to_overlay);

      ForwardRequest req = new ForwardRequest(machine, message);

      if (gateWayCache.ContainsKey(to_overlay) == true) {
        req.fire(to_overlay, gateWayCache[to_overlay]);
      }
      else {
        RequestQueue[to_overlay].Enqueue(req);
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: starting lookupService for " + to_overlay + "...");
        ReDiRNode.lookupService(to_overlay);
      }
    }

    public void fetchVia(string NameSpace, string resourcename, Usage_Code_Point codePoint) {

      FetchRequest req = new FetchRequest(machine, resourcename, codePoint);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace, gateWayCache[NameSpace]);
      }
      else {
        RequestQueue[NameSpace].Enqueue(req);
        ReDiRNode.lookupService(NameSpace);
      }
    }

    public void appAttachVia(string NameSpace, Destination dest, String DestinationOverlay) {

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        Arbiter.Activate(machine.ReloadConfig.DispatcherQueue, new IterativeTask<Destination, NodeId, String>
          (dest, gateWayCache[NameSpace], DestinationOverlay, machine.Transport.AppAttachProcedure));
      }
      else {
        ReDiRNode.lookupService(NameSpace);
      }


    }

  }
}
