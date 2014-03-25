using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Threading;
using TSystems.RELOAD;
using System.ComponentModel;
using TSystems.RELOAD.Extension;
using TSystems.RELOAD.Usage;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace ReloadGUI
{
    public partial class GUI : Window
    {
        // Event handler of the Fetch request can write the result in this property
        private string fetchSipResult;
        private ContextMenu cm = new ContextMenu();
        public string FetchSipResult
        {
            get { return fetchSipResult; }

            set { fetchSipResult = value; }
        }

        public List<Peer> peerList = new List<Peer>();
        int IDcounter = 0;
        public DispatcherTimer popuptimer = new DispatcherTimer();

        private Stream fileStream = null;

        // Event handler
        void LogTextChanged(object sender, TextChangedEventArgs e)
        {
            RichTextBox textView = (RichTextBox)sender;
            TextRange completeContent = new TextRange(textView.Document.ContentStart, textView.Document.ContentEnd);
            string logContent = completeContent.Text;

            // highlight the text to search
            if (FindLogTextBox.Text.Length > 0)
            {
                // http://msdn.microsoft.com/en-us/library/system.windows.documents.textpointer.aspx
                TextPointer position = textView.Document.ContentStart;
                while (position != null)
                {
                    if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        string textRun = position.GetTextInRun(LogicalDirection.Forward);
                        int indexInRun = textRun.IndexOf(FindLogTextBox.Text);
                        if (indexInRun >= 0)
                        {
                            TextPointer start = position.GetPositionAtOffset(indexInRun);
                            TextPointer end = start.GetPositionAtOffset(FindLogTextBox.Text.Length);
                            var textRange = new TextRange(start, end);
                            textRange.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Red);
                        }
                    }
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
        }

        public GUI()
        {
            InitializeComponent();
            popuptimer.Interval = TimeSpan.FromSeconds(2);
            //startPeer(overlayComboBox.Text, false);
            sipText.Text = "sip:test123@" + overlayComboBox.Text;
        }

        private Peer startPeer(String overlay, Boolean isGateway)
        {
            int port = Int32.Parse(portBox.Text);
            bool client = radioClient.IsChecked.Value;
            bool bootstrap = bootstrapBox.IsChecked.Value;
            bool webServer = webserverActiveBox.IsChecked.Value;
            if (webServer == true)
                webserverActiveBox.IsChecked = false;

            Peer newPeer = new Peer(port, isGateway, client, bootstrap, webServer, overlay);
            peerList.Add(newPeer);

            TabItem tabView = new TabItem();

            RichTextBox textView = new RichTextBox();
            textView.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            textView.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            textView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            // use find button
            //textView.TextChanged += new TextChangedEventHandler(LogTextChanged);

            tabView.Header = "Joining...";
            tabView.Content = textView;
            tabView.MouseRightButtonDown += new MouseButtonEventHandler(tabview_context);

            tabControl.Items.Add(tabView);
            tabControl.SelectedItem = tabView;

            newPeer.logTab = tabView;
            newPeer.TabID = IDcounter;
            //newPeer.OverlayName = OverlayName;
            //newPeer.storedDataTree = storedDataTree;
            newPeer.window = this;

            IDcounter++;

            if (autoIncBox.IsChecked == true)
            {
                portBox.Text = (UInt32.Parse(portBox.Text) + 1).ToString();
            }

            newPeer.start();

            return newPeer;

        }

        private void tabview_context(object sender, MouseButtonEventArgs e)
        {
            cm.Items.Clear();

            TabItem tab = (TabItem)sender;
            String nodeId = (String)tab.Header;

            cm.PlacementTarget = this;

            MenuItem item1 = new MenuItem();
            item1.Header = "Stop peer";
            item1.PreviewMouseLeftButtonDown += new MouseButtonEventHandler((Sender, E) => stopPeerEvent(sender, e, nodeId, tab));

            MenuItem item2 = new MenuItem();
            item2.Header = "Close peer";
            item2.PreviewMouseLeftButtonDown += new MouseButtonEventHandler((Sender, E) => closePeerEvent(sender, e, nodeId, tab));

            MenuItem item3 = new MenuItem();
            item3.Header = "Crash peer";
            item3.PreviewMouseLeftButtonDown += new MouseButtonEventHandler((Sender, E) => crashPeerEvent(sender, e, nodeId, tab));

            if ((String)tab.Header != "Joining...")
            {
                foreach (Peer p in peerList)
                    if (p.machine.ReloadConfig.LocalNodeID.ToString() == nodeId)
                    {
                        if (!p.machine.ReloadConfig.IsBootstrap)
                        {
                            if (p.machine.ReloadConfig.State != ReloadConfig.RELOAD_State.Exit)
                                cm.Items.Add(item1);

                            cm.Items.Add(item2);

                            if (p.machine.ReloadConfig.State != ReloadConfig.RELOAD_State.Exit)
                                cm.Items.Add(item3);
                            cm.IsOpen = true;
                        }
                    }
            }
        }

        private void stopPeerEvent(object sender, MouseButtonEventArgs e, String nodeId, TabItem tab)
        {
            Peer peer = null;
            foreach (Peer p in peerList)
            {
                if (p.machine.ReloadConfig.LocalNodeID.ToString() == nodeId)
                {
                    peer = p;
                    peer.machine.SendCommand("Leave");
                }
            }
        }

        private void crashPeerEvent(object sender, MouseButtonEventArgs e, String nodeId, TabItem tab)
        {
            Peer peer = null;
            foreach (Peer p in peerList)
            {
                if (p.machine.ReloadConfig.LocalNodeID.ToString() == nodeId)
                {
                    peer = p;
                    peer.machine.Finish();
                }
            }
        }

        private void closePeerEvent(object sender, MouseButtonEventArgs e, String nodeId, TabItem tab)
        {
            Peer peer = null;
            foreach (Peer p in peerList)
            {
                if (p.machine.ReloadConfig.LocalNodeID.ToString() == nodeId)
                {
                    peer = p;
                    peer.machine.SendCommand("Leave");

                    tabControl.Items.Remove(tab);
                    cm.Items.Clear();
                    cm.IsOpen = false;
                    break;
                }
            }

            ThreadPool.QueueUserWorkItem((o) =>
                {
                    Dispatcher.Invoke(
                        (Action)(() =>
                        {
                            while (peer.machine.ReloadConfig.State.ToString() != "Exit")
                                Thread.Sleep(50);
                            peerList.Remove(peer);
                        }));
                });
        }

        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            startPeer(overlayComboBox.Text, false);
        }

        private void radioPeer_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void radioClient_Checked(object sender, RoutedEventArgs e)
        {
            //bootstrapBox.IsChecked = false;
            //bootstrapBox.IsEnabled = false;
        }

        private void tabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //peerList.ElementAt<Peer>(tabControl.SelectedIndex).refreshStoredData();

            try
            {
                NTree<string> tree = peerList.ElementAt<Peer>(tabControl.SelectedIndex).getStoredData();
                List<string> replicas = peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine.Topology.Replicas;

                storedReplica.Items.Clear();
                foreach (string s in replicas)
                    storedReplica.Items.Add(s);

                updateTreeView(tree);
                OverlayName.Text = peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine.ReloadConfig.OverlayName;
                if (peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine is GWMachine)
                    OverlayName.Background = Brushes.Blue;
                else
                    OverlayName.Background = Brushes.White;
            }
            catch (Exception ex)
            {
            }

        }

        private void updateTreeView(NTree<string> tree)
        {
            storedDataTree.Items.Clear();
            storedDataTree.Items.Refresh();
            if (tree != null)
            {
                TreeViewItem root = new TreeViewItem();
                root.Header = tree.getData();
                storedDataTree.Items.Add(root);

                printTree(tree, root);
            }
        }

        private void printTree(NTree<string> node, TreeViewItem top)
        {
            int i = 1;
            top.IsExpanded = false;
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

        //private void redirButton_Click(object sender, RoutedEventArgs e)
        //{
        //    peerList.ElementAt<Peer>(tabControl.SelectedIndex).joinReDiR();
        //}

        private void storeSIPButton_Click(object sender, RoutedEventArgs e)
        {
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).store(sipText.Text, Usage_Code_Point.SIP_REGISTRATION);

        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {

            NTree<string> tree = peerList.ElementAt<Peer>(tabControl.SelectedIndex).getStoredData();

            List<string> replicas = peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine.Topology.Replicas;

            storedReplica.Items.Clear();
            foreach (string s in replicas)
                storedReplica.Items.Add(s);

            updateTreeView(tree);
        }

        private void lookupButton_Click(object sender, RoutedEventArgs e)
        {
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).ReDiRlookup();
        }

        private void gatewayButton_Click(object sender, RoutedEventArgs e)
        {
            Peer mainPeer = startPeer(overlayComboBox.Text, true);
            Peer interdomainPeer = startPeer("interdomain.org", true);


            GateWay gw = new GateWay((GWMachine)mainPeer.machine, (GWMachine)interdomainPeer.machine);
            ((GWMachine)mainPeer.machine).GateWay.InterdomainMessageProcessed += mainPeer.gateway_handleinterdomainmessage;
            ((GWMachine)interdomainPeer.machine).GateWay.InterdomainMessageProcessed += interdomainPeer.gateway_handleinterdomainmessage;
            //gw.start();
        }
        private void handoverButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (Peer x in peerList)
                x.machine.SendCommand("Maintenance");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {

            bool bootstrap = false;

            foreach (Peer p in peerList)
                if (p.machine.ReloadConfig.IsBootstrap)
                    bootstrap = true;

            if (peerList.Count > 0 && !bootstrap)
            {
                e.Cancel = true;
                CloseDialog closeDialog = new CloseDialog(peerList);
                closeDialog.Show();

                foreach (Peer p in peerList)
                {
                    if (!p.machine.ReloadConfig.IsBootstrap)
                        p.machine.SendCommand("Leave");
                }
            }
        }

        private void clearLogButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (TabItem test in tabControl.Items)
            {
                ((TextBox)test.Content).Clear();
            }
        }

        private void lookupService_Click(object sender, RoutedEventArgs e)
        {
            (new ReDiR(peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine)).lookupService(serviceName.Text);  //hack
        }

        private void registerService_Click(object sender, RoutedEventArgs e)
        {
            (new ReDiR(peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine)).registerService(serviceName.Text);  //hack

        }

        private void fetchSIPButton_Click(object sender, RoutedEventArgs e)
        {

            AutoResetEvent autoEvent = new AutoResetEvent(false);
            String result = "";

            peerList.ElementAt<Peer>(tabControl.SelectedIndex).fetch(sipText.Text, Usage_Code_Point.SIP_REGISTRATION);
            DFetchCompleted temp = null;
            temp = delegate(List<IUsage> usages)
            {
                foreach (var usage in usages)
                {
                    if (usage.CodePoint == Usage_Code_Point.SIP_REGISTRATION)
                    {
                        SipRegistration sipusage = (SipRegistration)usage;
                        switch (sipusage.Type)
                        {
                            case TSystems.RELOAD.Transport.SipRegistrationType.sip_registration_uri: // URI
                                result = sipusage.Data.sip_uri; // Ergebnis
                                break;
                            case TSystems.RELOAD.Transport.SipRegistrationType.sip_registration_route: // Destination
                                result = sipusage.Data.destination_list[0].ToString(); // Ergebnis (NodeId)
                                break;
                            default:
                                throw new NotSupportedException(
                                    String.Format("The type {0} is not supported!", sipusage.Type));
                        }
                        //
                        ((AutoResetEvent)autoEvent).Set();
                    }
                }
                return true;
            };
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine.FetchCompleted += temp;

            int millisecondsTimeout = 3000;
            if (autoEvent.WaitOne(millisecondsTimeout))
            {
                imagePathText.Text = result;
            }
            else
            {
                imagePathText.Text = "Timeout";
            }
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).machine.FetchCompleted -= temp;
        }

        private void webserverActiveBox_Checked(object sender, RoutedEventArgs e)
        {
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).InitWebServer();
        }

        private void browseImageButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            if (openFileDialog1.ShowDialog() == true)
            {
                try
                {
                    if ((fileStream = openFileDialog1.OpenFile()) != null)
                    {
                        imagePathText.Text = openFileDialog1.FileName;
                        ImageSource imageSource = new BitmapImage(new Uri(openFileDialog1.FileName));
                        loadedImageControl.Source = imageSource;
                    }
                }
                catch (Exception ex)
                {
                    imagePathText.Text = "Error: Could not read file from disk. Original error: " + ex.Message;
                }
            }
        }

        private void fetchImageButton_Click(object sender, RoutedEventArgs e)
        {
            fetchedImageControl.Source = null;
            string resourcename = imageNameText.Text;
            ImageStoreData result;
            result = peerList.ElementAt<Peer>(tabControl.SelectedIndex).fetchImage(resourcename);
            if (result.Data != null)
            {
                try
                {
                    MemoryStream ms = new MemoryStream(result.Data);
                    BitmapImage b = new BitmapImage();
                    b.BeginInit();
                    b.StreamSource = ms;
                    b.EndInit();
                    fetchedImageControl.Source = b;
                }
                catch (Exception ex)
                {
                    imagePathText.Text = "Exception: " + ex.Message;
                }
            }
            else
                imagePathText.Text = "Error: fetch failed!";
        }

        private void storeImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (fileStream == null)
            {
                imagePathText.Text = "no file loaded!";
                return;
            }
            fileStream.Seek(0, SeekOrigin.Begin);
            byte[] data;
            BitmapImage image = new BitmapImage();
            image = (BitmapImage)loadedImageControl.Source;
            string resourcename = imageNameText.Text;
            string imageName = resourcename;
            int width = image.PixelWidth;
            int height = image.PixelHeight;
            BinaryReader br = new BinaryReader(fileStream);
            data = br.ReadBytes((Int32)fileStream.Length);

            object[] args = new object[] { resourcename, imageName, width, height, data };
            peerList.ElementAt<Peer>(tabControl.SelectedIndex).storeImage(args);
        }

        private void On(object sender, MouseButtonEventArgs e)
        {

        }

        private void clearLoadedImage_Click(object sender, RoutedEventArgs e)
        {
            loadedImageControl.Source = null;
        }

        private void clearFetchedImage_Click(object sender, RoutedEventArgs e)
        {
            fetchedImageControl.Source = null;
        }

        private void overlayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sipText != null)
                sipText.Text = "sip:test123@" + (e.AddedItems[0] as ComboBoxItem).Content as string;
        }

        private void stateBox_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog close = new CloseDialog(peerList);
            close.Show();
        }

        private void On_FindLogButtonClick(object sender, RoutedEventArgs e)
        {
            RichTextBox textView = (RichTextBox)peerList.ElementAtOrDefault(tabControl.SelectedIndex).logTab.Content;

            TextRange completeContent = new TextRange(textView.Document.ContentStart, textView.Document.ContentEnd);
            string logContent = completeContent.Text;

            // highlight the text to search
            if (FindLogTextBox.Text.Length > 0)
            {
                // http://msdn.microsoft.com/en-us/library/system.windows.documents.textpointer.aspx
                TextPointer position = textView.Document.ContentStart;
                while (position != null)
                {
                    if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        string textRun = position.GetTextInRun(LogicalDirection.Forward);
                        int indexInRun = textRun.IndexOf(FindLogTextBox.Text);
                        if (indexInRun >= 0)
                        {
                            TextPointer start = position.GetPositionAtOffset(indexInRun);
                            TextPointer end = start.GetPositionAtOffset(FindLogTextBox.Text.Length);
                            var textRange = new TextRange(start, end);
                            textRange.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Orange);
                        }
                    }
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
        }

        private void On_ClearSearchButtonClick(object sender, RoutedEventArgs e)
        {
            FindLogTextBox.Text = "";
            RichTextBox logView = (RichTextBox)peerList.ElementAtOrDefault(tabControl.SelectedIndex).logTab.Content;
            TextRange completeContent = new TextRange(logView.Document.ContentStart, logView.Document.ContentEnd);
            completeContent.ClearAllProperties();
        }
    }

    public class ProgressWindow : Window
    {
        TextBox textBox = new TextBox();
        Label label = new Label { Content = "Closing all peers. This could take a while." };

        public ProgressWindow()
        {
            WindowStyle = System.Windows.WindowStyle.None;
            Height = 200;
            Width = 300;
            ShowInTaskbar = false;

            label.Width = 300;
            label.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 20, 0, 0);

            textBox.Height = 150;

            var stackPanel = new StackPanel { Orientation = Orientation.Vertical };
            stackPanel.Children.Add(label);
            stackPanel.Children.Add(textBox);
            Content = stackPanel;
        }

        public void SetText(String s)
        {
            textBox.AppendText(s);
        }
    }
}
