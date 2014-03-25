using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using TSystems.RELOAD;
using TSystems.RELOAD.Usage;
using System.Collections.Specialized;

namespace ReloadGUI {
  public class Server {
    private static System.Threading.AutoResetEvent listenForNextRequest = new System.Threading.AutoResetEvent(false);

    public Server() {
      _httpListener = new HttpListener();
    }

    private HttpListener _httpListener;

    public string Prefix { get; set; }

    public Machine machine { get; set; }

    public void Start() {
      if (String.IsNullOrEmpty(Prefix))
        throw new InvalidOperationException("No prefix has been specified");
      _httpListener.Prefixes.Clear();
      _httpListener.Prefixes.Add(Prefix);
      _httpListener.Start();
      System.Threading.ThreadPool.QueueUserWorkItem(Listen);
    }

    internal void Stop() {
      _httpListener.Stop();
      IsRunning = false;
    }

    public bool IsRunning { get; private set; }

    private void ListenerCallback(IAsyncResult result) {
      HttpListener listener = result.AsyncState as HttpListener;
      HttpListenerContext context = null;

      if (listener == null)
        // Nevermind 
        return;

      try {
        context = listener.EndGetContext(result);
      }
      catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine(ex.ToString());
        return;
      }
      finally {
        listenForNextRequest.Set();
      }
      if (context == null)
        return;
      ProcessRequest(context);
    }

    private void ProcessRequest(HttpListenerContext context) {
      
      int sip_type = 1; //uri
      NameValueCollection temp = context.Request.QueryString;
      machine.ReloadConfig.SipUri = temp["resourcename"]; //resourcename => hashed to resourceid
      string[] args;
      switch(temp["operation"].ToUpper())
      {
        case "STORE":
          args = new string[] { temp["forwarduri"], temp["resourcename"] };
          //lock (machine) 
          {
            machine.GatherCommandsInQueue("Store", Usage_Code_Point.SIP_REGISTRATION, sip_type, null, true, args);
            machine.SendCommand("Store");
          }
          break;
        case "FETCH":
         args = new string[] {temp["resourcename"] };
         //lock (machine)
         {
           machine.GatherCommandsInQueue("Fetch", Usage_Code_Point.SIP_REGISTRATION, sip_type, null, true, args);
           machine.SendCommand("Fetch");
         }
          break;
        default:
          break;
      }
      HttpListenerResponse response = context.Response;
      response.StatusCode = 200;
      //response.ContentType = "text/xml";
      response.Close();
     
    }

    // Loop here to begin processing of new requests. 
    private void Listen(object state) {
      while (_httpListener.IsListening) {
        _httpListener.BeginGetContext(new AsyncCallback(ListenerCallback), _httpListener);
        listenForNextRequest.WaitOne();
      }
    }

  }
}
