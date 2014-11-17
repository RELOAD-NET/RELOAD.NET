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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using TSystems.RELOAD.ForwardAndLinkManagement;
using Microsoft.Ccr.Core;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using System.Threading;
using System.Globalization;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Enroll;
using FTEntry = TSystems.RELOAD.Topology.TopologyPlugin.RoutingTable.FTableEntry;

namespace TSystems.RELOAD {

  public delegate bool DStoreCompleted(ReloadDialog dialog);
  public delegate bool DFetchCompleted(List<IUsage> usageResult);
  public delegate void DAppAttachCompleted(IceCandidate ice);

  /// <summary>
  /// A delegate that updates each interested component about status of
  /// stack.
  /// </summary>
  /// <param name="state">The state of the stack.</param>    
  public delegate void ReloadStateUpdate(ReloadConfig.RELOAD_State state);

  /// <summary>
  /// RELOAD main worker thread, major state machine is implemented here
  /// </summary>
  public class Machine {

    #region Properties

    private ReloadConfig m_ReloadConfig = null;

    public ReloadConfig ReloadConfig {
      get { return m_ReloadConfig; }
      //set { m_ReloadConfig = value; } seems to be never used --alex
    }

    private UsageManager m_UsageManager = null;

    /// <summary>
    /// The Usage manager handles all what belongs to usages.
    /// </summary>
    public UsageManager UsageManager {
      get { return m_UsageManager; }
      set { m_UsageManager = value; }
    }    

    public bool Contains(List<IceCandidate> list, List<BootstrapServer> bsslist) {
      foreach (IceCandidate ice in list) {
        foreach (BootstrapServer bs in bsslist) {
          if (ice.addr_port.ipaddr.ToString() == bs.Host
              && ice.addr_port.port == bs.Port)
            return true;
        }
      }
      return false;
    }

    List<BootstrapServer> m_BootstrapServerList = new List<BootstrapServer>();

    public List<BootstrapServer> BootstrapServer {
      get { return m_BootstrapServerList; }
      set { m_BootstrapServerList = value; }
    }

    private TopologyPlugin m_topology;
    public TopologyPlugin Topology {
      get { return m_topology; }
      set { m_topology = value; }
    }

    private MessageTransport m_transport;

    public MessageTransport Transport {
      get { return m_transport; }
      set { m_transport = value; }
    }
    private IForwardLinkManagement m_interface_flm;

    public IForwardLinkManagement Interface_flm {
      get { return m_interface_flm; }
      set { m_interface_flm = value; }
    }
    private ForwardingLayer m_forwarding;

    public ForwardingLayer Forwarding {
      get { return m_forwarding; }
      set { m_forwarding = value; }
    }


    /* 
     * These Lists are used to preliminary gather specifiers or storeDatas
     * that will be send with a single request 
     */

    private List<StoredDataSpecifier> gatheredSpecifiers;

    private List<StoreKindData> gatheredStoreDatas;

	 /* 
	 * These Queues are used to queue multiple gatheredSpecifiers
	 * and gatheredStoreDatas. Used by GatherCommandsInQueue Method 
	 */

	 private Port<List<StoredDataSpecifier>> gatheredSpecifiersQueue;

   private Port<List<StoreKindData>> gatheredStoreDatasQueue;

	 /* 
	 * These Dictionaries are used for store and fetch commands which
	 * needs to be send to a gateway first. The reference between the 
	 * Gateway NodeId and the List<StoreKindData> is kept until the 
	 * request ist submitted. 
	 */

	 private Dictionary<List<StoreKindData>, NodeId> storeViaGateway;

	 private Dictionary<List<StoredDataSpecifier>, NodeId> fetchViaGateway;

    /*  If you make your BW interruptable (setting WorkerSupportsCancellation to true) and your 
     *  BW periodically checks for CancellationPending, then your main thread can issue a
     *  BW.CancelAsync() when it's time to stop the current task. After making sure it really 
     *  stopped, restart with new data.
     */
    private BackgroundWorker m_worker_thread;

    /// <summary>
    /// Delegate fpr Reload state updates
    /// </summary>
    private ReloadStateUpdate stateUpdates;
    public ReloadStateUpdate StateUpdates {
      get { return stateUpdates; }
      set { stateUpdates = value; }
    }

    #endregion

    #region Events

    /// <summary>
    /// Fires if a store request has finished
    /// </summary>
    public event DStoreCompleted StoreCompleted;

    /// <summary>
    /// Fires if a fetch request has finished
    /// </summary>
    public event DFetchCompleted FetchCompleted;

    /// <summary>
    /// Fires if an AppAttach has finished
    /// </summary>
    public event DAppAttachCompleted AppAttachCompleted;

    #endregion

    #region Constructor Start/Stop Worker

    public Machine() {
      m_ReloadConfig = new ReloadConfig(this);

      m_UsageManager = new UsageManager();

      gatheredSpecifiers = new List<StoredDataSpecifier>();

      gatheredStoreDatas = new List<StoreKindData>();
	  
	  gatheredSpecifiersQueue = new Port<List<StoredDataSpecifier>>();

    gatheredStoreDatasQueue = new Port<List<StoreKindData>>();

	  storeViaGateway = new Dictionary<List<StoreKindData>, NodeId>();

	  fetchViaGateway = new Dictionary<List<StoredDataSpecifier>, NodeId>();

    }

    public void StartWorker() {
      m_worker_thread = new BackgroundWorker();
      m_worker_thread.WorkerSupportsCancellation = true;
      m_worker_thread.DoWork += new DoWorkEventHandler(this.DoWork);
      m_worker_thread.RunWorkerCompleted += new RunWorkerCompletedEventHandler(this.Completed);
      m_worker_thread.WorkerReportsProgress = true;
      m_worker_thread.ProgressChanged += new ProgressChangedEventHandler(this.ProgressChanged);

      stateUpdates += new ReloadStateUpdate(onStateUpdate);

      m_worker_thread.RunWorkerAsync();
    }

    public void StopWorker() {
      if (m_worker_thread != null)
        m_worker_thread.CancelAsync();
    }

    private void ProgressChanged(object sender, ProgressChangedEventArgs e) {
      if (e.ProgressPercentage == 100) {
        InitUsageManager();        
      }
    }

    private void onStateUpdate(ReloadConfig.RELOAD_State state) {
    }

    #endregion

    #region Command takers

    /// <summary>
    /// This method gathers all Kinds that will be sent withon one single request.
    /// @precondition: The storing data MUST have the same Resource Name.
    /// @precondition: The the command MUST be the same. (e.g., ONLY store | fetch)
    /// </summary>
    /// <param name="command">Store | Fetch</param>
    /// <param name="codePoint">The Identifier for the Usage to be performed</param>
    /// <param name="type">Some Usages define differents types (see sip usage). Place here that value in Usage spec.</param>
    /// <param name="arguments">argument[0] Resource Name
    /// 
    ///                         If SIP Registration: args[1] = uri | contact prefs
    ///                         If DisCo Registration: args[1] TODO
    ///                         If Access List: args[1] = kinId
    ///                                         args[2] = from_user
    ///                                         args[3] = to_user
    ///                                         args[4] = allow_delegation</param>
    public void GatherCommands(string command, Usage_Code_Point codePoint,
        int type, params object[] arguments) {
      IUsage usage = null;
      StoredDataSpecifier specifier = null;
      /* Check  plausibility of user commands */
      if (m_ReloadConfig.CommandQueuePort.ItemCount > 0 &&
         command != (string)m_ReloadConfig.CommandQueuePort.Test()) {
        throw new ArgumentException(String.Format(
            "Your command {0} is not the same as previews command {1}.",
            command,
            (string)m_ReloadConfig.CommandQueuePort.Test()));
      }
      /* Process store command */
      if (command.Equals("Store")) {
        StoreKindData kindData;
        usage = m_UsageManager.CreateUsage(codePoint, type, arguments);
        kindData = new StoreKindData(usage.KindId,
                                     0, // mok! TODO generation management
                                     new StoredData(usage.Encapsulate(true)));
        gatheredStoreDatas.Add(kindData);
      }
      /* Process fetch command */
      if (command.Equals("Fetch")) {
        string[] str_arguments = arguments.Select(item => (string)item).ToArray();
        UInt32 kind = UsageManager.CreateUsage(codePoint, null, null).KindId;
        // To further garantee SIP Usage with Telefonenumbers
        if (codePoint == Usage_Code_Point.SIP_REGISTRATION &&
            str_arguments[0].StartsWith("+")) {
          string FetchUrl = "";
          ReloadConfigResolve res = new ReloadConfigResolve(m_ReloadConfig);          
          FetchUrl = res.ResolveNaptr(str_arguments[0]);
          if (FetchUrl == null) {
            ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
              "DNS Enum fallback to sip uri analysis");
            FetchUrl = str_arguments[0];
            FetchUrl = FetchUrl.TrimStart(' ');
            FetchUrl = FetchUrl.Replace(" ", "");
            FetchUrl = "sip:" + FetchUrl + "@" + m_ReloadConfig.OverlayName;
          }
          arguments[0] = FetchUrl;
        }
        specifier = m_UsageManager.createSpecifier(kind, str_arguments);
        gatheredSpecifiers.Add(specifier);
      }
    }
	
    public void GatherCommandsInQueue(string command, Usage_Code_Point codePoint, int type, NodeId viaGateway, bool CommandFinished = false, params object[] arguments)  //TODO: combine to one single method
    {
      IUsage usage = null;
      StoredDataSpecifier specifier = null;

      if (command.Equals("Store"))
      {
			StoreKindData kindData;
			usage = m_UsageManager.CreateUsage(codePoint, type, arguments);
			//foreach (UInt32 kindId in usage.Kinds)    TODO:
			{
                //kindData = new StoreKindData(kindId, 0, new StoredData(usage.Encapsulate(kindId, true)));
                kindData = new StoreKindData(usage.KindId, 0, new StoredData(usage.Encapsulate(true)));
				gatheredStoreDatas.Add(kindData);
				//m_ReloadConfig.GatheringList.Add(kindData);
			}
			if (CommandFinished == true)
			{
				if (viaGateway != null)
					storeViaGateway.Add(gatheredStoreDatas, viaGateway);
				gatheredStoreDatasQueue.Post(gatheredStoreDatas);
				gatheredStoreDatas = new List<StoreKindData>();
			}
		}
		if (command.Equals("Fetch"))
		{
            //UInt32[] kinds = UsageManager.CreateUsage(codePoint, null, null).Kinds; TODO: cleanup
            UInt32 kind = UsageManager.CreateUsage(codePoint, null, null).KindId;
			// To further garantee SIP Usage with Telefonenumbers
			if (codePoint == Usage_Code_Point.SIP_REGISTRATION && arguments[0].ToString().StartsWith("+"))
			{
				string FetchUrl = "";
				ReloadConfigResolve res = new ReloadConfigResolve(m_ReloadConfig);
                FetchUrl = res.ResolveNaptr(arguments[0].ToString());
				if (FetchUrl == null)
				{
					ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, "DNS Enum fallback to sip uri analysis");
                    FetchUrl = arguments[0].ToString();
					FetchUrl = FetchUrl.TrimStart(' ');
					FetchUrl = FetchUrl.Replace(" ", "");
          FetchUrl = "sip:" + FetchUrl + "@" + m_ReloadConfig.OverlayName;
					arguments[0] = FetchUrl;
				}
			}
            //foreach (UInt32 kindId in kinds)      TODO: cleanup
			{
                specifier = m_UsageManager.createSpecifier(kind, arguments);
				gatheredSpecifiers.Add(specifier);
			}
			if (CommandFinished == true)
			{
				if (viaGateway != null)
					fetchViaGateway.Add(gatheredSpecifiers, viaGateway);
				gatheredSpecifiersQueue.Post(gatheredSpecifiers);
				gatheredSpecifiers = new List<StoredDataSpecifier>();
			}
			
		}     
	}


    public IEnumerator<ITask> StoreTask() {
      m_ReloadConfig.CommandQueuePort.Post("Store");
      yield break;
    }

    
    public IEnumerator<ITask> CommandTask(String sCommand) {
      m_ReloadConfig.CommandQueuePort.Post(sCommand);
      yield break;
    }

    
    public void Store() {
    Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(StoreTask));
    }

    public void SendCommand(String sCommand) {
        try
        {
            Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask<String>(sCommand, CommandTask));
        }
        catch (Exception ex)
        {

        }
    }

    #endregion

    #region CommendCheck Task

    public IEnumerator<ITask> CommandCheckTask() {
      while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
        //lock (m_ReloadConfig.CommandQueue) 
        {
          //TODO: geht nur für GatherCommandsInQueue
          if (ReloadConfig.CommandQueuePort.ItemCount > 0 /*&& ReloadConfig.CommandQueuePort.ItemCount == (gatheredSpecifiersQueue.ItemCount + gatheredStoreDatasQueue.ItemCount)*/) {
            string s;
            //ReloadConfig.CommandQueuePort.Test(out s);
            while (ReloadConfig.CommandQueuePort.Test(out s)) {
              
              if (s == null)
                continue;

              if (s == "PreJoin") {
                ReloadConfig.IamClient = false;

                if (ReloadConfig.IsBootstrap)
                  ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "This is the bootstrap server");
                else {
                  Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask<List<BootstrapServer>>(m_BootstrapServerList, m_transport.PreJoinProdecure));
                }
              }
              else if (s.StartsWith("Store")) {

                //Queue or not?
                //if (gatheredStoreDatasQueue.ItemCount > 0)
                  gatheredStoreDatas = (List<StoreKindData>)gatheredStoreDatasQueue;

                string resourceName = gatheredStoreDatas[0].Values[0].Value.GetUsageValue.ResourceName;
                ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE,
                  String.Format("Calling Store: {0}", resourceName));
                List<StoreKindData> storeKindData = new List<StoreKindData>();
                storeKindData.AddRange(gatheredStoreDatas);
                m_transport.StoreDone = new Port<ReloadDialog>();

                if (storeViaGateway.ContainsKey(gatheredStoreDatas)) { // --joscha
                  NodeId via = storeViaGateway[gatheredStoreDatas];
                  storeViaGateway.Remove(gatheredStoreDatas);
                  Arbiter.Activate(ReloadConfig.DispatcherQueue,
                    new IterativeTask<string, List<StoreKindData>,
                      NodeId>(resourceName, storeKindData, via, m_transport.Store));
                }
                else
                  Arbiter.Activate(ReloadConfig.DispatcherQueue,
                    new IterativeTask<string, List<StoreKindData>>(
                        resourceName, storeKindData, m_transport.Store));

                Arbiter.Activate(m_ReloadConfig.DispatcherQueue,
                    Arbiter.Receive(true, m_transport.StoreDone, dialog => {
                      if (StoreCompleted != null) StoreCompleted(dialog);
                    }));
                gatheredStoreDatas.Clear();
              }
              else if (s.StartsWith("Fetch")) {
                List<StoredDataSpecifier> specifier;    //necessary to pass a valid reference to m_transport.Fetch

                //Queue or not?
                if (gatheredSpecifiersQueue.ItemCount > 0)
                  specifier = (List<StoredDataSpecifier>)gatheredSpecifiersQueue;
                else
                  specifier = gatheredSpecifiers;

                if (specifier == null) {
                  break;  //TODO:
                }
                string FetchUrl = specifier[0].ResourceName;

                if (FetchUrl.Length > 0) {
                  ReloadConfig.ConnEstStart = DateTime.Now;
                  ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      String.Format("Calling Fetch: {0}", FetchUrl));

                  /* Ports used to notify */
                  m_transport.FetchDone = new Port<List<IUsage>>();
                  m_transport.AppAttachDone = new Port<IceCandidate>();

                  List<StoredDataSpecifier> specifiers = new List<StoredDataSpecifier>(); //copy of specifier needed for fetch task
                  specifiers.AddRange(specifier);

                  if (fetchViaGateway.ContainsKey(specifier)) { // --joscha
                    NodeId via = fetchViaGateway[specifier];
                    fetchViaGateway.Remove(specifier);
                    Arbiter.Activate(ReloadConfig.DispatcherQueue,
                      new IterativeTask<string, List<StoredDataSpecifier>, NodeId>(
                        FetchUrl, specifiers, via, m_transport.Fetch));
                  }
                  else
                    Arbiter.Activate(ReloadConfig.DispatcherQueue,
                        new IterativeTask<string, List<StoredDataSpecifier>>(
                            FetchUrl, specifiers, m_transport.Fetch));
                }
                else
                  ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      String.Format("Empty Fetch command!"));

                /* Fetch completed notify everybody */
                Arbiter.Activate(ReloadConfig.DispatcherQueue,
                    Arbiter.Receive(true, m_transport.FetchDone,
                        delegate(List<IUsage> usages) {
                          if (FetchCompleted != null) FetchCompleted(usages);
                          gatheredSpecifiers.Clear();
                        }));
                /* Corresponding AppAttach completed, notify everybody */
                Arbiter.Activate(ReloadConfig.DispatcherQueue,
                    Arbiter.Receive(true, m_transport.AppAttachDone, ice => {
                      if (AppAttachCompleted != null) AppAttachCompleted(ice);

                    }));
              }
              else if (s == "Leave") {
                ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                    String.Format("Received \"Leave\" command"));

                if (ReloadConfig.IsBootstrap) {
                  ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                      String.Format("Bootstrap Server cannot leave"));
                }
                else {
                    if (ReloadConfig.IamClient)
                    {
                        Arbiter.Activate(ReloadConfig.DispatcherQueue,
                            new IterativeTask<string, List<StoreKindData>>(ReloadConfig.SipUri,
                                new List<StoreKindData>(), m_transport.Store));
                    }
                    else
                    {
                        if (ReloadConfig.DispatcherQueue != null)
                            Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask<bool>(true, m_transport.HandoverKeys));
                    }

                  //ReloadConfig.IamClient = true;
                }
                // peer looses bootstrap flag, this is important for rejoin
                //TKTODO Rejoin of bootstrap server not solved
              }
              else if (s == "Exit") {
                Finish();
              }
              else if (s == "Maintenance") {
                if (!ReloadGlobals.fMaintenance)
                  Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(Maintenance));
                ReloadGlobals.fMaintenance = !ReloadGlobals.fMaintenance;
              }
              else if (s == "Info") {
                PrintNodeInfo(m_topology, true);
              }
            }
            //ReloadConfig.CommandQueue.Clear();
          }

            //ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_KEEPALIVE, "");

          if (ReloadConfig.State != RELOAD.ReloadConfig.RELOAD_State.Exit)
          {
              Port<DateTime> timeoutPort = new Port<DateTime>();
              ReloadConfig.DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0, 100 /* ms 100ms default */), timeoutPort);
              yield return Arbiter.Receive(false, timeoutPort, x => { });
          }
        }
      }
    }

    #endregion

    void PrintNodeInfo(TopologyPlugin topology, bool extended) {

      if (topology == null)
        return;

      TimeSpan deltaT = DateTime.Now - ReloadConfig.Statistics.StartTime;

      topology.routing_table.PrintNeigborState();

      ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format(@"
Statistics
Time up: {0}
Rx - Total bytes: {1}, current Bits per Second: {2}
Tx - Total bytes: {3}, current Bits per Second: {4}",
          deltaT.ToString(),
          ReloadConfig.Statistics.TotalBytesRx,
          (ReloadConfig.Statistics.RxThroughputPerSec * 1000 * 8).ToString("f0", CultureInfo.InvariantCulture),
          ReloadConfig.Statistics.TotalBytesTx,
          (ReloadConfig.Statistics.TxThroughputPerSec * 1000 * 8).ToString("f0", CultureInfo.InvariantCulture)));

      if (extended) {
        string fingerTableString = "";


        /*            
         * fingerTableString = topology.routing_table.Finger.ToString();

                    fingerTableString = @"
        Finger table: (Start: Finger)";
                    for (int i = 0; i < fingerTable.Length; i++) {
                        if (fingerTable[i] != null)
                            fingerTableString += string.Format("\r\n{0}: {1}", topology.Id + ReloadGlobals.BigIntPow2Array[i], fingerTable[i]);
                        else
                            fingerTableString += string.Format("\r\n<empty>");
                    }

        */


        fingerTableString += @"
Successor cache:";
        for (int i = topology.routing_table.GetSuccessorCount(false) - 1; i >= 0; i--) {
          Node node = topology.routing_table.GetNode(topology.routing_table.GetSuccessorId(i));
          fingerTableString += string.Format("\r\n    S{0}: {1} {2}", i, node.ToString(), topology.routing_table.GetStatusShortLetter(node.Id));
        }
        fingerTableString += @"
Me               :";
        fingerTableString += string.Format("\r\n    ME: {0}", topology.LocalNode.ToString());

        fingerTableString += @"
Predecessor cache:";
        for (int i = 0; i < topology.routing_table.GetPredecessorCount(false); i++) {
          Node node = topology.routing_table.GetNode(topology.routing_table.GetPredecessorId(i));
          fingerTableString += string.Format("\r\n    P{0}: {1} {2}", i, node.ToString(), topology.routing_table.GetStatusShortLetter(node.Id));
        }
        Console.WriteLine(fingerTableString);
      }
    }

    public IEnumerator<ITask> Reporting() {
      while (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit) {
        try {
          ReloadConfig.Statistics.Successor = m_topology.routing_table.GetApprovedSuccessor();
          ReloadConfig.Statistics.Predecessor = m_topology.routing_table.GetApprovedPredecessor();

          if (ReloadConfig.Statistics.KeyList == null)
            ReloadConfig.Statistics.KeyList = new List<string>();
          else
            ReloadConfig.Statistics.KeyList.Clear();

          if (m_topology.Storage != null &&
              m_topology.Storage.StoredValues != null &&
              m_topology.Storage.StoredValues.Count >= 0) {
            foreach (string key in m_topology.Storage.StoredKeys)
              ReloadConfig.Statistics.KeyList.Add(key);
          }
          /*switch (value.store_kind_data.stored_data.type)
          {
              case SipRegistrationType.sip_registration_route:
                  if(value.store_kind_data.stored_data.destination_list.Count > 0)
                      ReloadConfig.Statistics.KeyList.Add(value.store_kind_data.stored_data.destination_list[0].destination_data.node_id.ToString());
                  break;
              case SipRegistrationType.sip_registration_uri:
                  ReloadConfig.Statistics.KeyList.Add(value.store_kind_data.stored_data.sip_uri);
                  break;
          }*/
        }
        catch (Exception ex) {
          ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Reporting: " + ex.Message);
        }

        ReloadConfig.Statistics.Reporting();
        //ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_KEEPALIVE, "");

        Port<DateTime> timeoutPort = new Port<DateTime>();
        ReloadConfig.DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0,
          ReloadGlobals.REPORTING_PERIOD), timeoutPort);
        yield return Arbiter.Receive(false, timeoutPort, x => { });
      }
    }

    public IEnumerator<ITask> UpdateCycle() {

      try {
        if (ReloadConfig.Document.Overlay.configuration.chordpingintervalSpecified)
          ReloadGlobals.CHORD_UPDATE_INTERVAL = ReloadConfig.Document.Overlay.configuration.chordupdateinterval;
      }
      catch { }

      while (ReloadGlobals.fMaintenance && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        Port<DateTime> timeoutPort = new Port<DateTime>();
        ReloadConfig.DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0, ReloadGlobals.CHORD_UPDATE_INTERVAL * 100), timeoutPort);
        yield return Arbiter.Receive(false, timeoutPort, x => { });

        /* 
         * 9.6.4.1.  Updating neighbor table

           A peer MUST periodically send an Update request to every peer in its
           Connection Table.  The purpose of this is to keep the predecessor and
           successor lists up to date and to detect failed peers.  The default
           time is about every ten minutes, but the enrollment server SHOULD set
           this in the configuration document using the "chord-reload-update-
           interval" element (denominated in seconds.)  A peer SHOULD randomly
           offset these Update requests so they do not occur all at once.
         */

        try {
          if (!m_ReloadConfig.IamClient) {
            m_topology.routing_table.SendUpdateToAllNeighbors();
            //m_topology.routing_table.SendUpdatesToAllFingers();
          }
        }
        catch (Exception ex) {
          ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "UpdateCycle: " + ex.Message);
          throw ex;
        }
      }
    }

    public IEnumerator<ITask> Maintenance() {
      try {
        if (m_ReloadConfig.Document.Overlay.configuration.chordpingintervalSpecified)
          ReloadGlobals.CHORD_PING_INTERVAL = m_ReloadConfig.Document.Overlay.configuration.chordpinginterval;
      }
      catch { }


      while (ReloadGlobals.fMaintenance && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
        Port<DateTime> timeoutPort = new Port<DateTime>();
        ReloadConfig.DispatcherQueue.EnqueueTimer(new TimeSpan(0, 0, 0, 0, ReloadGlobals.CHORD_PING_INTERVAL * 500), timeoutPort);
        yield return Arbiter.Receive(false, timeoutPort, x => { });

        try {
          if (m_ReloadConfig.State < ReloadConfig.RELOAD_State.Shutdown) {
            CheckObsoletConnections();
            SendPingToAllNeighbors();
            if (!ReloadConfig.IamClient)
              FixFingers();
            if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joined || m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joining)
            {
              Arbiter.Activate(ReloadConfig.DispatcherQueue,
                new IterativeTask<bool>(false, m_transport.HandoverKeys));
            }
          }
        }
        catch (Exception ex) {
          ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "Maintenance: " + ex.Message);
          throw ex;
        }
      }
    }

    private void CheckObsoletConnections() {
#if !COMPACT_FRAMEWORK
      try {
        if (m_interface_flm.ConnectionTable == null)
          return;

        var conns = m_interface_flm.ConnectionTable;
        bool isNeighbour = false;
        bool isFinger = false;
        List<ReloadConnectionTableInfoElement> closedConns = new List<ReloadConnectionTableInfoElement>();
        foreach (var connection in conns) {
          isNeighbour = m_topology.routing_table.isNewNeighbour(connection.NodeID);
          isFinger = m_topology.routing_table.isFinger(connection.NodeID);

          if (!isNeighbour && !isFinger &&
             (DateTime.Now - connection.LastActivity).TotalSeconds >= ReloadGlobals.CHORD_PING_INTERVAL + 30) {
            connection.AssociatedSocket.Disconnect(false);
            closedConns.Add(connection);
          }
        }
        foreach (var connection in closedConns)
          conns.Remove(connection);
      }
      catch (Exception e) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, e.Message);
      }
#endif
    }

    private void SendPingToAllNeighbors() {
      List<NodeId> neighbors = new List<NodeId>();
      TopologyPlugin.RoutingTable rt = m_topology.routing_table;

      if (m_ReloadConfig.IamClient) {
        if (ReloadConfig.AdmittingPeer != null)
          neighbors.Add(ReloadConfig.AdmittingPeer.Id);
      }
      else {
        neighbors.AddRange(rt.Successors);
        neighbors.AddRange(rt.Predecessors);
        neighbors = rt.removeDuplicates(neighbors);
      }

      foreach (NodeId nodeid in neighbors) {
        if (nodeid != null && rt.IsAttached(nodeid)) {
          /* do we need a new connectivity check? */
          foreach (ReloadConnectionTableInfoElement rce in m_interface_flm.ConnectionTable) {
            if (rce.NodeID == nodeid) {
              if ((DateTime.Now - rce.LastActivity).TotalMilliseconds < 2 * ReloadGlobals.MAINTENANCE_PERIOD) {
                Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask<Destination, PingOption>(new Destination(nodeid), PingOption.standard, m_transport.SendPing));
                break;
              }
            }
          }
        }
      }
    }

    private void FixFingers() {
      Topology.TopologyPlugin.RoutingTable rt = m_topology.routing_table;
      List<FTEntry> fingers = m_topology.routing_table.rmDuplicateFingers(
        rt.FingerTable);

      if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joined)  {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Fix Fingers now!");
        /* Add each valid node of the fingertable to the next hop list */
        foreach (FTEntry fte in fingers) {
          /* do we need a new connectivity check? */
          if ((DateTime.Now - fte.dtLastSuccessfullFinger).TotalMilliseconds >
            2 * ReloadGlobals.MAINTENANCE_PERIOD) {
            Arbiter.Activate(ReloadConfig.DispatcherQueue,
              new IterativeTask<Destination, PingOption>(new Destination(fte.Finger),
              PingOption.finger, m_transport.SendPing));
          }
        }
      }
    }

    private void Completed(object sender, RunWorkerCompletedEventArgs e) {

    }


    public void Finish() {
      ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Shutdown...");

      // delete local certificate
      X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
      store.Open(OpenFlags.ReadWrite);
      store.Remove(m_ReloadConfig.MyCertificate);
      store.Close();
      ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Deleted Local Certificate");

      ReloadConfig.State = ReloadConfig.RELOAD_State.Exit;
      stateUpdates(ReloadConfig.RELOAD_State.Exit);

      try {
        if (m_interface_flm != null) {
          m_interface_flm.ShutDown();
          m_interface_flm = null;
        }
      }
      catch (Exception ex) {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Link Shutdown: " + ex.Message);
      }

      try {
        ReloadConfig.State = 0;

        /*                if (ReloadConfig.DispatcherQueue != null)
                            ReloadConfig.DispatcherQueue.Dispose();
        */
        if (ReloadConfig.Dispatcher != null)
          ReloadConfig.Dispatcher.Dispose();

        ReloadConfig.State = ReloadConfig.RELOAD_State.Exit;

        //              ReloadConfig.DispatcherQueue = null;
        ReloadConfig.Dispatcher = null;
      }
      catch (Exception ex) {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Dispatcher Dispose: " + ex.Message);
      }
    }


    private void DoWork(object sender, DoWorkEventArgs e) {

      //Console.WriteLine("Machine.Start");
      //the major building blocks of RELOAD
#if false
            System.Diagnostics.Debugger.Break();
#endif
      if (!Init())
        this.m_worker_thread.CancelAsync();
    }

    public bool Init() {

      try {
        if (ReloadGlobals.IgnoreSSLErrors)
          IgnoreSSLErrors();

        m_transport = new MessageTransport();

        if (ReloadGlobals.TLS)
          m_interface_flm = new ReloadFLM(ReloadConfig);
        else
          m_interface_flm = new SimpleFLM(ReloadConfig);

        ReloadConfig.Statistics.SetParams(m_interface_flm);
        m_interface_flm.ReloadFLMEventHandler += 
          new ReloadFLMEvent(m_transport.rfm_ReloadFLMEventHandler);

        ReloadConfig.State = ReloadConfig.RELOAD_State.Init;
        stateUpdates(ReloadConfig.RELOAD_State.Init);

        ReloadConfigResolve resolve = new ReloadConfigResolve(ReloadConfig);

        resolve.ReadConfig();
        if (ReloadGlobals.TLS)
          resolve.EnrollmentProcedure();
        else
          resolve.SimpleNodeIdRequest();

        m_interface_flm.Init();
        m_ReloadConfig.AccessController = new AccessController(m_ReloadConfig);
        m_topology = new TopologyPlugin(this);
        if (!m_topology.Init(this))
          return false;

        m_forwarding = new ForwardingLayer(this);
        m_transport.Init(this);

        //ReloadConfig.State = ReloadConfig.RELOAD_State.Configured;
        //stateUpdates(ReloadConfig.RELOAD_State.Configured);
        BootStrapConfig();

        m_ReloadConfig.StartJoining = DateTime.Now;
        if (m_ReloadConfig.IamClient)
          m_ReloadConfig.StartJoinMobile = DateTime2.Now;
        if (!ReloadConfig.IsBootstrap)
          Arbiter.Activate(ReloadConfig.DispatcherQueue, 
            new IterativeTask<List<BootstrapServer>>(m_BootstrapServerList,
            m_transport.PreJoinProdecure));

//        m_worker_thread.ReportProgress(100); --joscha
        InitUsageManager();
        ReloadConfig.State = ReloadConfig.RELOAD_State.Configured;
        stateUpdates(ReloadConfig.RELOAD_State.Configured);

        /* reporting service */
        Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(Reporting));
        /* chord-ping-interval */
        Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(Maintenance));
        /* chord-update-interval */
        Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(UpdateCycle));
        Arbiter.Activate(ReloadConfig.DispatcherQueue, new IterativeTask(CommandCheckTask));
      }
      catch (Exception ex) {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Init: " + ex.Message);
      }
      return true;
    }

    private void InitUsageManager() {
        m_UsageManager.Init(this);
        m_UsageManager.RegisterUsage(new SipRegistration(m_UsageManager));
        m_UsageManager.RegisterUsage(new ImageStoreUsage(m_UsageManager));
      // TODO
      //m_UsageManager.RegisterUsage(new Haw.DisCo.DisCoUsage(m_UsageManager));

      //m_UsageManager.RegisterUsage(new Haw.DrisCo.ShaReUsage(m_UsageManager));
	    m_UsageManager.RegisterUsage(new RedirServiceProvider(m_UsageManager));
        m_UsageManager.RegisterUsage(new CertificateStore(true, m_UsageManager));
        m_UsageManager.RegisterUsage(new CertificateStore(false, m_UsageManager));
    }

    private void BootStrapConfig() {
      if (ReloadConfig.Document != null) {
#if true 
          foreach (bootstrapnode bstrnode in ReloadConfig.Document.Overlay.configuration.bootstrapnode)
          m_BootstrapServerList.Add(new BootstrapServer(bstrnode.address, bstrnode.port));
#else //TKTEST IETF
        m_BootstrapServerList.Add(new BootstrapServer("80.153.249.37", 6084));
#endif
      }
      else {
        if (ReloadGlobals.BootstrapHost != "") {
          m_BootstrapServerList.Add(new BootstrapServer(
            ReloadGlobals.BootstrapHost, ReloadGlobals.BootstrapPort));
        }
      }

      if (m_topology.LocalNode == null)
        return;

      if (Contains(m_topology.LocalNode.IceCandidates, m_BootstrapServerList)) {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "This is a bootstrap node, no join needed");
        ReloadConfig.IsBootstrap = true;
        ReloadConfig.LastJoinedTime = DateTime.Now;
      }
    }

    private void ForceOnline() {
      try {
        //this will force an online (GPRS E/3G) connection
        Uri req_uri = new Uri(ReloadGlobals.ReportURL);
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(req_uri);
        HttpWebResponse res = (HttpWebResponse)req.GetResponse();
        res.Close();
      }
      catch {
        ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Online check failed"));
      }
    }

#if !WINDOWS_PHONE
	// The server certificate validation callback is not supported

    /// <summary>
    /// Internal object used to allow setting WebRequest.CertificatePolicy to 
    /// not fail on Cert errors
    /// </summary>
    internal class AcceptAllCertificatePolicy : ICertificatePolicy {
      public AcceptAllCertificatePolicy() { }
      public bool CheckValidationResult(ServicePoint sPoint, X509Certificate cert, WebRequest wRequest, int certProb) {
        // *** Always accept
        return true;
      }
    }

#endif

    void IgnoreSSLErrors() {
#if !WINDOWS_PHONE
      // The server certificate validation callback is not supported

      System.Net.ServicePointManager.ServerCertificateValidationCallback +=
      delegate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                    System.Security.Cryptography.X509Certificates.X509Chain chain,
                    System.Net.Security.SslPolicyErrors sslPolicyErrors) {
        return true; // **** Always accept
      };

#endif
    }

    public void SetTracer() {
      throw new NotImplementedException();
    }
  }
}

