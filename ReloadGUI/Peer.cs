using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using TSystems.RELOAD;
using TSystems.RELOAD.Extension;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Usage;
using System.Net;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Threading;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Windows.Documents;



namespace ReloadGUI
{



    public class NTree<T>
    {
        T data;
        LinkedList<NTree<T>> children;

        public NTree(T data)
        {
            this.data = data;
            children = new LinkedList<NTree<T>>();
        }

        public void addChild(T data)
        {
            children.AddFirst(new NTree<T>(data));
        }

        public NTree<T> getChildOrCreate(T input)
        {
            if (this.getChild(1) == null)
            {

                this.addChild(input);
                return this.getChild(1);
            }
            else
            {
                for (int i = 1; this.getChild(i) != null; i++)
                {
                    if (EqualityComparer<T>.Default.Equals(this.getChild(i).getData(), input))
                    {
                        return this.getChild(i);

                    }
                }

            }
            this.addChild(input);
            return this.getChild(1);
        }

        public NTree<T> getChild(int i)
        {
            foreach (NTree<T> n in children)
                if (--i == 0) return n;
            return null;
        }

        public T getData()
        {
            return data;
        }
    }


    public class Peer : INotifyPropertyChanged
    {
        public Machine machine;

        //TextWriter logFile;
        //TextWriter measureFile;

        GatewayRequestHandler gwRequestHandler;

        public TabItem logTab { get; set; }

        public GUI window { get; set; }//workaround
        public int TabID { get; set; }

        // for closing dialog
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, e);
        }

        public Peer(int listenPort, Boolean isGWPeer, Boolean isClient, Boolean isBootstrap, Boolean activateWbServer, string overlay)
        {
            if (isGWPeer == true)
            {
                machine = new GWMachine();

            }
            else
                machine = new Machine();
            InitReloadEngine(listenPort, isGWPeer, isClient, isBootstrap, overlay);
            //if (activateWbServer == true)
            //  InitWebServer();
        }

        public void InitWebServer()
        {
            Server webserver = new Server();
            webserver.machine = machine;
            webserver.Prefix = "http://*:8080/";

            webserver.Start();

        }

        public void InitReloadEngine(int listenPort, Boolean isGWPeer, Boolean isClient, Boolean isBootstrap, string overlay)
        {
            machine.ReloadConfig.Logger = new ReloadConfig.LogHandler(Logger);
            machine.ReloadConfig.ListenPort = listenPort;
            machine.ReloadConfig.IamClient = isClient;
            machine.ReloadConfig.IsBootstrap = isBootstrap;
            machine.ReloadConfig.OverlayName = overlay;
            machine.ReloadConfig.DontCheckSSLCert = true;

            machine.StoreCompleted += new DStoreCompleted(machine_StoreCompleted);
            machine.StateUpdates += new ReloadStateUpdate(machine_StateUpdate);

            //machine.FetchCompleted += new DFetchCompleted(machine_FetchCompleted);

            Config(overlay);

            gwRequestHandler = new GatewayRequestHandler(machine);
        }

        public void start()
        {
            machine.StartWorker();
        }

        public void gateway_handleinterdomainmessage(string destination_overlay, bool destination_reached)
        {

            updatePopupColor(Color.FromRgb(255, 215, 0), Color.FromRgb(255, 215, 0));
            if (destination_reached)
                updatePopup(destination_overlay, "destination_reached", "<-");
            else
                updatePopup(destination_overlay, "destination_reached", "->");
        }


        void machine_StateUpdate(ReloadConfig.RELOAD_State state)
        {

            if (state == ReloadConfig.RELOAD_State.Configured)
            {
                updateLog(TabID, "State: " + state.ToString() + " initial TransactionID " + machine.ReloadConfig.TransactionID);
                updateNodeID(TabID, machine.ReloadConfig.LocalNodeID.ToString());
                machine.Topology.Storage.ResourceStored += new TSystems.RELOAD.Storage.StorageModul.DResourceStored(StorageModule_ResourceStored);
            }

            else if (state == ReloadConfig.RELOAD_State.Joined)
                updateColor(TabID, Brushes.Green);

            else if (state == ReloadConfig.RELOAD_State.Exit)
            {
                updateColor(TabID, Brushes.Red);
            }
            else
                updateColor(TabID, Brushes.Black);

            OnPropertyChanged(new PropertyChangedEventArgs("state"));
        }

        void redir_FetchCompleted(string nameSpace, NodeId id)
        {


        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dialog"></param>
        /// <returns></returns>
        bool machine_StoreCompleted(ReloadDialog dialog)
        {


            //NTree<string> tree = getStoredData();



            return true;
        }

        void StorageModule_ResourceStored(ResourceId resId, StoreKindData kindData)
        {
            NTree<string> tree = getStoredData();

            //updateStoredData(tree);
            StringBuilder temp = new StringBuilder();
            foreach (StoredData sd in kindData.Values)
            {
                temp.Append(sd.Value.GetUsageValue.Report() + "\n");
            }
            updatePopup(resId.ToString(), temp.ToString(), machine.Topology.Storage.StoredKeys.Count.ToString());

            if (ReloadGUI.Properties.Settings.Default.autoUpdate_Data_Tree)
                updateStoredData(tree);

        }

        public void Config(string overlay)
        {
            ReloadGlobals.Client = ReloadGUI.Properties.Settings.Default.Client;
            ReloadGlobals.TLS = ReloadGUI.Properties.Settings.Default.TLS;
            ReloadGlobals.TimeStamps = ReloadGUI.Properties.Settings.Default.TimeStamps;
            ReloadGlobals.TLS_PASSTHROUGH = ReloadGUI.Properties.Settings.Default.TLS_Passtrough;
            ReloadGlobals.IgnoreSSLErrors = ReloadGUI.Properties.Settings.Default.IgnoreSSLErrors;
            ReloadGlobals.ReportEnabled = ReloadGUI.Properties.Settings.Default.ReportEnabled;
            ReloadGlobals.ReportIncludeConnections = ReloadGUI.Properties.Settings.Default.ReportIncludeConnections;
            ReloadGlobals.TRACELEVEL = (ReloadGlobals.TRACEFLAGS)ReloadGUI.Properties.Settings.Default.TraceLevel;
            ReloadGlobals.ForceLocalConfig = ReloadGUI.Properties.Settings.Default.ForceLocalConfig;
            ReloadGlobals.DNS_Address = ReloadGUI.Properties.Settings.Default.DNS_Address;
            ReloadGlobals.UseDNS = ReloadGUI.Properties.Settings.Default.UseDNS;
            ReloadGlobals.AllowPrivateIP = ReloadGUI.Properties.Settings.Default.AllowPrivateIP;
            //ReloadGlobals.OverlayName = overlay;        //TODO: this should never be used so remove
            ReloadGlobals.MaxRetransmissions = ReloadGUI.Properties.Settings.Default.MaxRetransmissions;
            ReloadGlobals.TLS_PASSTHROUGH = ReloadGUI.Properties.Settings.Default.TLS_Passtrough;
            ReloadGlobals.FRAGMENT_SIZE = ReloadGUI.Properties.Settings.Default.Fragment_Size;
            ReloadGlobals.FRAGMENTATION = ReloadGUI.Properties.Settings.Default.Fragmentation;
            ReloadGlobals.only_RSA_NULL_MD5 = ReloadGUI.Properties.Settings.Default.only_RSA_NULL_MD5;

            // STUN
            ReloadGlobals.StunIP1 = ReloadGUI.Properties.Settings.Default.StunIP1;
            ReloadGlobals.StunPort1 = ReloadGUI.Properties.Settings.Default.StunPort1;
            ReloadGlobals.StunIP2 = ReloadGUI.Properties.Settings.Default.StunIP2;
            ReloadGlobals.StunPort2 = ReloadGUI.Properties.Settings.Default.StunPort2;

            // NO_ICE
            ReloadGlobals.UseNoIce = ReloadGUI.Properties.Settings.Default.UseNoIce;

            // UPnP
            ReloadGlobals.UseUPnP = ReloadGUI.Properties.Settings.Default.UseUPnP;

            // SR
            ReloadGlobals.UseSR = ReloadGUI.Properties.Settings.Default.UseSR;

            // SO
            ReloadGlobals.UseSO = ReloadGUI.Properties.Settings.Default.UseSO;


            //we can't support multiple certs per commandline in this MDI app, we need enrollment server here
            if (ReloadGlobals.TLS)
                ReloadGlobals.ForceLocalConfig = false;
        }

        private void updateLog(int tabid, string s)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            RichTextBox logView = (RichTextBox)logTab.Content;
                                            //logView.AppendText(s + '\n');
                                            logView.AppendText(s + '\r'); // use this for RichTextBox
                                            if (window.autoscrollBox.IsChecked == true)
                                                logView.ScrollToEnd();
                                        }));
        }

        private void updateColor(int tabid, SolidColorBrush brush)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            logTab.Foreground = brush;
                                        }));
        }

        private void updatePopup(String lineOne, string s, string totalStores)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            TextBlock block = (TextBlock)((Grid)window.Popup.Child).Children.Cast<UIElement>().First(e => Grid.GetRow(e) == 1 && Grid.GetColumn(e) == 1);
                                            block.Text = lineOne;
                                            block = (TextBlock)((Grid)window.Popup.Child).Children.Cast<UIElement>().First(e => Grid.GetRow(e) == 2 && Grid.GetColumn(e) == 1);
                                            block.Text = s;
                                            block = (TextBlock)((Grid)window.Popup.Child).Children.Cast<UIElement>().First(e => Grid.GetRow(e) == 3 && Grid.GetColumn(e) == 1);
                                            block.Text = totalStores;
                                            window.Popup.IsOpen = true;
                                            window.Popup.StaysOpen = false;
                                            window.popuptimer.Start();
                                            window.popuptimer.Tick += delegate
                                            {
                                                window.Popup.IsOpen = false;
                                            };
                                        }));
        }

        private void updatePopupColor(Color from, Color to)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            LinearGradientBrush color = new LinearGradientBrush(from, to, 90.0);
                                            ((Grid)window.Popup.Child).Background = color;
                                            window.Popup.IsOpen = true;
                                            window.Popup.StaysOpen = true;

                                        }));
        }

        private void updateLogColor(Color from, Color to)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            LinearGradientBrush color = new LinearGradientBrush(from, to, 90.0);

                                            logTab.Background = color;
                                            TextBox logView = (TextBox)logTab.Content;
                                            logView.Background = color;
                                        }));
        }

        private void updateColor(Color from, Color to)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            LinearGradientBrush color = new LinearGradientBrush(from, to, 90.0);
                                            window.Background = color;

                                        }));
        }

        private void updateNodeID(int tabid, string s)
        {
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            logTab.Header = s;
                                        }));
        }

        private void updateStoredData(NTree<string> tree)
        {
            NodeId id = machine.Topology.Id;
            logTab.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                        new Action(() =>
                                        {
                                            if (((TabItem)window.tabControl.SelectedItem).Header.ToString() == id.ToString())
                                            {
                                                window.storedDataTree.Items.Clear();
                                                window.storedDataTree.Items.Refresh();
                                                if (tree != null)
                                                {
                                                    TreeViewItem root = new TreeViewItem();
                                                    root.Header = tree.getData();
                                                    window.storedDataTree.Items.Add(root);
                                                    printTree(tree, root);
                                                }
                                            }
                                        }));
        }

        private void printTree(NTree<string> node, TreeViewItem top)
        {
            int i = 1;
            //top.IsExpanded = true;
            if (node != null)
            {
                while (node.getChild(i) != null)
                {
                    TreeViewItem tempItem = new TreeViewItem();

                    tempItem.Header = node.getChild(i).getData();
                    top.Items.Add(tempItem);

                    printTree(node.getChild(i), tempItem);
                    i++;
                }
            }
        }

        public NTree<string> getStoredData()
        {
            NTree<string> basis = null;
            try
            {
                int replicaCount = machine.Topology.Replicas.Count;
                int storedValues = machine.Topology.Storage.StoredKeys.Count - replicaCount;
                if (machine.Topology != null && storedValues != 0)
                {
                    basis = new NTree<string>("Data: TOTAL COUNT " + storedValues);
                    foreach (string resid in machine.Topology.Storage.StoredKeys)
                    {
                        if (!machine.Topology.Replicas.Contains(resid))
                        {
                            foreach (StoreKindData kindData in machine.Topology.Storage.GetStoreKindData(resid))
                            {
                                if (kindData.Kind == 1234)
                                {
                                    NTree<string> root = basis.getChildOrCreate("SIP_REGISTRATION");
                                    root.addChild("ResID: TOTAL COUNT " + kindData.Values.Count);
                                    root = root.getChild(1);
                                    root = root.getChildOrCreate(resid);
                                    foreach (StoredData storedData in kindData.Values)
                                    {
                                        root = basis.getChild(1).getChild(1).getChild(1);
                                        root.addChild("SipRegistrationData");

                                        IUsage use = storedData.Value.GetUsageValue;

                                        SipRegistration SipReg = ((SipRegistration)use);

                                        root = root.getChild(1);
                                        root.addChild(SipReg.Report());
                                    }


                                }
                                if (kindData.Kind == ReloadGlobals.REDIR_KIND_ID)
                                {
                                    NTree<string> root = basis.getChildOrCreate("REDIR");
                                    root.addChild("ResID");
                                    root = root.getChild(1);
                                    root = root.getChildOrCreate(resid);

                                    foreach (StoredData storedData in kindData.Values)
                                    {
                                        root = basis.getChild(1).getChild(1).getChild(1);
                                        root.addChild("RedirServiceProviderData");

                                        IUsage use = storedData.Value.GetUsageValue;

                                        RedirServiceProviderData RedirData = ((RedirServiceProvider)use).Data;


                                        root = root.getChild(1);
                                        root.addChild("Node");
                                        root.getChild(1).addChild(RedirData.node.ToString()); //TODO:
                                        root.addChild("Level");
                                        root.getChild(1).addChild(RedirData.level.ToString());
                                        root.addChild("Namespace");
                                        root.getChild(1).addChild(RedirData.nameSpace);
                                        root.addChild("ServiceProvider");
                                        root.getChild(1).addChild(RedirData.serviceProvider.ToString());
                                    }
                                }
                                if (kindData.Kind == 0xc0ffee)
                                {
                                    NTree<string> root = basis.getChildOrCreate("ImageStore");
                                    root.addChild("ResID");
                                    root = root.getChild(1);
                                    root = root.getChildOrCreate(resid);

                                    foreach (StoredData storedData in kindData.Values)
                                    {
                                        root = basis.getChild(1).getChild(1).getChild(1);
                                        root.addChild("ImageStoreData");

                                        IUsage use = storedData.Value.GetUsageValue;

                                        ImageStoreData data = ((ImageStoreUsage)use).Data;


                                        root = root.getChild(1);
                                        root.addChild("ImageName");
                                        root.getChild(1).addChild(data.Name); //TODO:
                                        //root.addChild("ImageUri");
                                        //root.getChild(1).addChild(data.);
                                        root.addChild("NodeId");
                                        root.getChild(1).addChild(data.NodeId.ToString());
                                    }
                                }
                            }
                        }
                    }
                }
                return basis;
            }
            catch (Exception e)
            {

            }
            return null;
        }

        public enum RequestType
        {
            store = 0,
            fetch = 1,
            attach = 2,
        }

        public void store(string resourcename, Usage_Code_Point codePoint)
        {
            string[] args = new string[] { resourcename };
            int sip_type = 2;

            if (false)
            {
                for (int i = 0; i < 100; i++)
                {
                    string[] arg = new string[] { "sip:forwarduri@xyz.de", i.ToString() };
                    machine.GatherCommandsInQueue("Store", Usage_Code_Point.SIP_REGISTRATION, 1, null, true, arg);
                    machine.SendCommand("Store");
                }
            }
            else if (false == resourcename.Contains(machine.ReloadConfig.OverlayName) && resourcename.Contains("@"))
            { // different DestinationOverlay
                machine.ReloadConfig.SipUri = resourcename;
                gwRequestHandler.storeVia("GATEWAYNODE", codePoint, sip_type, args);
            }
            else
            {
                machine.ReloadConfig.SipUri = resourcename;
                machine.GatherCommandsInQueue("Store", codePoint, sip_type, null, true, args);
                machine.SendCommand("Store");
            }
        }

        public void storeImage(object[] args)
        {
            machine.GatherCommandsInQueue("Store", Usage_Code_Point.IMAGE_STORE, 0, null, true, args);
            machine.SendCommand("Store");
        }

        public void fetch(string resourcename, Usage_Code_Point codePoint)
        {
            string[] args = new string[] { resourcename };
            int sip_type = 2;
            if (false)
            {
                for (int i = 0; i < 100; i++)
                {
                    string[] arg = new string[] { i.ToString() };
                    machine.GatherCommandsInQueue("Fetch", Usage_Code_Point.SIP_REGISTRATION, 1, null, true, arg);
                    machine.SendCommand("Fetch");
                }
            }
            else if (false == resourcename.Contains(machine.ReloadConfig.OverlayName) && resourcename.Contains("@"))
            { //TODO:
                gwRequestHandler.fetchVia("GATEWAYNODE", codePoint, sip_type, args);

                DFetchCompleted temp = null;
                temp = delegate(List<IUsage> usages)
                {
                    foreach (var usage in usages)
                    {
                        if (usage.CodePoint == Usage_Code_Point.SIP_REGISTRATION)
                        {
                            SipRegistration sipusage = (SipRegistration)usage;
                            //if (sipusage.Data.destination_list[0] == new Destination(new ResourceId(resourcename))) {
                            updateLog(TabID, "machine_FetchCompleted: SIP_REGISTRATION " + usage.Report());
                            updateLog(TabID, "NEXT STEP APPATTACH");
                            string destinationOverlay = resourcename.Substring(resourcename.IndexOf("@") + 1);
                            gwRequestHandler.appAttachVia("GATEWAYNODE", sipusage.Data.destination_list[0], destinationOverlay);
                            machine.FetchCompleted -= temp;
                            //}
                        }
                        else if (usage.CodePoint == Usage_Code_Point.NULL_USAGE && usage is NoResultUsage)
                        {
                            NoResultUsage nores = (NoResultUsage)usage;
                            if (nores.ResourceName == resourcename)
                                machine.FetchCompleted -= temp;

                        }
                    }
                    return true;
                };

                machine.FetchCompleted += temp;


            }
            else
            {
                machine.GatherCommandsInQueue("Fetch", codePoint, sip_type, null, true, args);
                machine.SendCommand("Fetch");
            }
        }

        public ImageStoreData fetchImage(string resourcename)
        {
            AutoResetEvent autoEvent = new AutoResetEvent(false);
            ImageStoreData result = new ImageStoreData();
            string[] args = new string[] { resourcename };
            machine.GatherCommandsInQueue("Fetch", Usage_Code_Point.IMAGE_STORE, 0, null, true, args);
            DFetchCompleted temp = null;

            temp = delegate(List<TSystems.RELOAD.Usage.IUsage> usages)
            {
                foreach (var usage in usages)
                {
                    if (usage.CodePoint == TSystems.RELOAD.Usage.Usage_Code_Point.IMAGE_STORE)
                    {
                        ImageStoreUsage imagestoreusage = (ImageStoreUsage)usage;

                        result = new ImageStoreData(imagestoreusage.Data.NodeId, imagestoreusage.Data.Name, imagestoreusage.Data.Width, imagestoreusage.Data.Height, imagestoreusage.Data.Data);

                        machine.FetchCompleted -= temp;

                        //
                        ((AutoResetEvent)autoEvent).Set();
                    }
                    else if (usage.CodePoint == Usage_Code_Point.NULL_USAGE && usage is NoResultUsage)
                    {
                        NoResultUsage nores = (NoResultUsage)usage;
                        if (nores.ResourceName == resourcename)
                            machine.FetchCompleted -= temp;

                    }
                }
                return true;
            };

            machine.FetchCompleted += temp;
            machine.SendCommand("Fetch");

            int millisecondsTimeout = 2000;
            bool signalReceived = autoEvent.WaitOne(millisecondsTimeout);
            if (!signalReceived)
                updateLog(TabID, "Timeout after " + millisecondsTimeout + "ms. Fetch on ressource " + resourcename + " not successfull.");
            return result;
        }

        public void ReDiRstore()
        {
            //redirNode.store(machine.Topology.LocalNode.Id, "GATEWAYNODE");
        }

        public void ReDiRlookup()
        {
            //redirNode.lookup(machine.Topology.LocalNode.Id, "GATEWAYNODE", null);
        }
        public void leave()
        {
            this.machine.SendCommand("Leave");
            Console.WriteLine("Trying to leave now");
        }
        public void exit()
        {
            this.machine.SendCommand("Exit");
        }

        void Logger(ReloadGlobals.TRACEFLAGS traces, string s)
        {
            //if (traces == ReloadGlobals.TRACEFLAGS.T_USAGE || traces == ReloadGlobals.TRACEFLAGS.T_ERROR || traces == ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION || traces == ReloadGlobals.TRACEFLAGS.T_REDIR)//|| traces == ReloadGlobals.TRACEFLAGS.T_INF
            if (traces != ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION && traces != ReloadGlobals.TRACEFLAGS.T_SOCKET && /*traces != ReloadGlobals.TRACEFLAGS.T_TRANSPORT &&*/ traces != ReloadGlobals.TRACEFLAGS.T_FH && s.Contains("Update") == false && s.Contains("Ping") == false)
                updateLog(0, s);
            //else
            else if (traces == ReloadGlobals.TRACEFLAGS.T_ERROR)
                updateLog(0, s);
            //
        }


    }
}
