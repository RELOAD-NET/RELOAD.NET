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
      protected Usage_Code_Point CodePoint;
      protected int type;
      protected string[] args;
      
      protected Machine machine;

      public string DestinationOverlay = null;

      public Request(Machine machine, Usage_Code_Point codePoint = 0, int type=0, string[] args = null) {
        this.machine = machine;
        this.CodePoint = codePoint;
        this.type = type;
        this.args = args;
      }

      public abstract void fire(string nameSpace, NodeId id);
    }

    public class FetchRequest : Request {

      public FetchRequest(Machine machine, Usage_Code_Point codePoint = 0, int type=0, string[] args = null)
        : base(machine, codePoint, type, args) {
      }

      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: Fetch via " + nameSpace);

        machine.GatherCommandsInQueue("Fetch", this.CodePoint, type, id, true, this.args);
        machine.SendCommand("Fetch");
      }
    }

    public class StoreRequest : Request {

      public StoreRequest(Machine machine, Usage_Code_Point codePoint = 0, int type=0, string[] args = null)
        : base(machine, codePoint, type, args) {
      }


      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: Store via " + nameSpace);

        machine.GatherCommandsInQueue("Store", this.CodePoint, this.type, id, true, this.args);
        machine.SendCommand("Store");
      }
    }

    public class AppAttachRequest : Request {

      string destination_overlay;
      Destination dest;
      public AppAttachRequest(Machine machine, Destination dest, string destination_overlay)
        : base(machine) {
          this.destination_overlay = destination_overlay;
          this.dest = dest;
      }

      public override void fire(string nameSpace, NodeId id)    //TODO: name
      {
        Arbiter.Activate(machine.ReloadConfig.DispatcherQueue, new IterativeTask<Destination, NodeId, String>
          (dest, id, destination_overlay, machine.Transport.AppAttachProcedure));
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
        machine.Transport.receive_message(msg);
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

    bool redir_LookupFailed(string nameSpace) {
      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_LookupFailed: " + " nameSpace: " + nameSpace);
      RequestQueue[nameSpace].Clear();

      return true;
    }


    public void forwardVia(string NameSpace, ReloadMessage message) {

      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: going to forward form to ServiceProvider" + NameSpace);

      message.security_block = new SecurityBlock(machine.ReloadConfig, machine.ReloadConfig.AccessController.MyIdentity);
      message.security_block.SignMessage(ReloadGlobals.OverlayHash, //TODO: remove overlayhash from glals
       message.TransactionID.ToString(), message.reload_message_body);
      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, message.reload_message_body.RELOAD_MsgCode.ToString() + " resigned (new SecurityBlock)");
      
      ForwardRequest req = new ForwardRequest(machine, message);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace, gateWayCache[NameSpace]);
      }
      else {
        RequestQueue[NameSpace].Enqueue(req);
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "GatewayRequestHandler: starting lookupService for " + NameSpace + "...");
        ReDiRNode.lookupService(NameSpace);
      }
    }


    public void fetchVia(string NameSpace,  Usage_Code_Point codePoint, int type, string [] args) {

      FetchRequest req = new FetchRequest(machine, codePoint, type, args);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace, gateWayCache[NameSpace]);
      }
      else {
        RequestQueue[NameSpace].Enqueue(req);
        ReDiRNode.lookupService(NameSpace);
      }
    }

    public void storeVia(string NameSpace, Usage_Code_Point codePoint, int type, string[] args) {

      StoreRequest req = new StoreRequest(machine, codePoint, type, args);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace, gateWayCache[NameSpace]);
      }
      else {
        RequestQueue[NameSpace].Enqueue(req);
        ReDiRNode.lookupService(NameSpace);
      }
    }

    public void appAttachVia(string NameSpace, Destination dest, string DestinationOverlay) {

      AppAttachRequest req = new AppAttachRequest(machine, dest, DestinationOverlay);

      if (gateWayCache.ContainsKey(NameSpace) == true) {
        req.fire(NameSpace,gateWayCache[NameSpace]);
      }
      else {
        ReDiRNode.lookupService(NameSpace);
      }


    }

  }
}
