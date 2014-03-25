using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ReloadGUI
{
    /// <summary>
    /// Interaction logic for CloseDialog.xaml
    /// </summary>
    public partial class CloseDialog : Window
    {
        ObservableCollectionEx<Peer> peerList;

        public CloseDialog(List<Peer> list)
        {
            InitializeComponent();
            peerList = new ObservableCollectionEx<Peer>();
            foreach (Peer p in list)
                peerList.Add(p);
            lstView.ItemsSource = CreatePeerList();

            ((INotifyPropertyChanged)peerList).PropertyChanged += new PropertyChangedEventHandler(PropertyChanged);
        }

        private List<PeerState> CreatePeerList()
        {
            List<PeerState> liste = new List<PeerState>();
            bool keepAlive = false;

            foreach (Peer p in peerList)
                if (p.machine.ReloadConfig.State != TSystems.RELOAD.ReloadConfig.RELOAD_State.Exit)
                    keepAlive = true;

            if (!keepAlive)
                Application.Current.Shutdown();

            foreach (Peer p in peerList)
            {
                liste.Add(new PeerState { peer = p.machine.ReloadConfig.LocalNodeID.ToString(), state = p.machine.ReloadConfig.State.ToString() });
            }
            return liste;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            lstView.ItemsSource = CreatePeerList();
        }

        public void PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                var dispatcher = lstView.Dispatcher;
                dispatcher.Invoke(new Action(() =>
                {

                    lstView.ItemsSource = CreatePeerList();
                }));
            }
            catch(Exception)
            {
            }

        }

        private void forceExit_Click(object sender, RoutedEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }
    }

    public class ObservableCollectionEx<T> : ObservableCollection<T> where T : INotifyPropertyChanged
    {

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            Unsubscribe(e.OldItems);
            Subscribe(e.NewItems);
            base.OnCollectionChanged(e);
        }

        protected override void ClearItems()
        {
            foreach (T element in this)
                element.PropertyChanged -= ContainedElementChanged;

            base.ClearItems();
        }

        private void Subscribe(IList iList)
        {
            if (iList != null)
            {
                foreach (T element in iList)
                    element.PropertyChanged += ContainedElementChanged;
            }
        }

        private void Unsubscribe(IList iList)
        {
            if (iList != null)
            {
                foreach (T element in iList)
                    element.PropertyChanged -= ContainedElementChanged;
            }

        }

        private void ContainedElementChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e);
        }
    }

    public class PeerState
    {
        public String peer { get; set; }
        public String state { get; set; }

        public PeerState()
        {
            peer = state = null;
        }

        public PeerState(String p, String s)
        {
            peer = p;
            state = s;
        }
    }
}
