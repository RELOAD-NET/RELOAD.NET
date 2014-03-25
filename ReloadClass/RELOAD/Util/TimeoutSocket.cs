using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TSystems.RELOAD.Util
{
    public class TimeoutSocket
    {

        // public
        public static void Connect(Socket connectingSocket, IPEndPoint connectEndPoint, int connectTimeout)
        {
            ConnectParams connectParams = new ConnectParams(connectingSocket, connectEndPoint, connectTimeout);

            Thread connectingThread = new Thread(new ParameterizedThreadStart(ConnectThread));
            connectingThread.Start(connectParams);

            connectingThread.Join();
        }

        public static Socket Accept(Socket listeningSocket, int acceptTimeout)
        {
            AcceptParams acceptParams = new AcceptParams(listeningSocket, acceptTimeout);

            Thread acceptingThread = new Thread(new ParameterizedThreadStart(AcceptThread));
            acceptingThread.Start(acceptParams);

            acceptingThread.Join();

            return acceptParams.AcceptedSocket;
        }


        // private
        private static void ConnectThread(object data)
        {
            ConnectParams connectParams = (ConnectParams)data;

            if (connectParams.ConnectingSocket == null || connectParams.ConnectIpEndPoint == null)
                return;

            try
            {
                ManualResetEvent mre = new ManualResetEvent(false);

                ConnectCallbackParams connectCallbackParams = new ConnectCallbackParams(connectParams.ConnectingSocket, mre);

                mre.Reset();
                connectParams.ConnectingSocket.BeginConnect(connectParams.ConnectIpEndPoint, ConnectCallback, connectCallbackParams);

                bool connectCompleted = mre.WaitOne(connectParams.ConnectTimeout);

                if (!connectCompleted)
                {
                    connectCallbackParams.Aborted = true;
                    EndPoint temp = new IPEndPoint(((IPEndPoint)connectParams.ConnectingSocket.LocalEndPoint).Address, 0);
                        //((IPEndPoint)connectParams.ConnectingSocket.LocalEndPoint).Port);
                    connectParams.ConnectingSocket.Close();   // <= this triggers the callback function
                    mre.WaitOne();  // wait again, until callback is completed
                    connectParams.ConnectingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    connectParams.ConnectingSocket.Bind(temp); 
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }

        private static void ConnectCallback(IAsyncResult ar)
        {
            ConnectCallbackParams connectCallbackParams = (ConnectCallbackParams)ar.AsyncState;

            try
            {
                if (!connectCallbackParams.Aborted)
                    connectCallbackParams.ConnectingSocket.EndConnect(ar);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                connectCallbackParams.Mre.Set();
            }

        }

        private static void AcceptThread(object data)
        {
            AcceptParams acceptParams = (AcceptParams)data;

            if (acceptParams.ListeningSocket == null)
                return;

            try
            {
                ManualResetEvent mre = new ManualResetEvent(false);

                AcceptCallbackParams acceptCallbackParams = new AcceptCallbackParams(acceptParams.ListeningSocket, mre);

                mre.Reset();

                acceptParams.ListeningSocket.BeginAccept(AcceptCallback, acceptCallbackParams);

                bool acceptCompleted = mre.WaitOne(acceptParams.AcceptTimeout);

                if (!acceptCompleted)
                {
                    acceptCallbackParams.Aborted = true;
                    acceptParams.ListeningSocket.Close();   // <= this triggers the callback function
                    mre.WaitOne();  // wait again, until callback is completed
                }


                acceptParams.AcceptedSocket = acceptCallbackParams.AcceptedSocket;


            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            AcceptCallbackParams acceptCallbackParams = (AcceptCallbackParams)ar.AsyncState;

            try
            {
                if (!acceptCallbackParams.Aborted)
                {
                    acceptCallbackParams.AcceptedSocket = acceptCallbackParams.ListeningSocket.EndAccept(ar);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                acceptCallbackParams.Mre.Set();
            }

        }

        private class ConnectParams
        {
            private Socket m_connectingSocket;
            private IPEndPoint m_connectIpEndPoint;
            private int m_connectTimeout;

            public int ConnectTimeout
            {
                get { return m_connectTimeout; }
            }

            public IPEndPoint ConnectIpEndPoint
            {
                get { return m_connectIpEndPoint; }
            }

            public Socket ConnectingSocket
            {
                get { return m_connectingSocket; }
                set { m_connectingSocket = value; }
            }


            public ConnectParams(Socket connectingSocket, IPEndPoint connectIpEndPoint, int connectTimeout)
            {
                m_connectingSocket = connectingSocket;
                m_connectIpEndPoint = connectIpEndPoint;
                m_connectTimeout = connectTimeout;
            }

        }

        private class AcceptParams
        {
            private Socket m_listeningSocket;
            private Socket m_acceptedSocket;
            private int m_acceptTimeout;

            public int AcceptTimeout
            {
                get { return m_acceptTimeout; }
            }

            public Socket ListeningSocket
            {
                get { return m_listeningSocket; }
            }

            public Socket AcceptedSocket
            {
                get { return m_acceptedSocket; }
                set { m_acceptedSocket = value; }
            }

            public AcceptParams(Socket listeningSocket, int acceptTimeout)
            {
                m_listeningSocket = listeningSocket;
                m_acceptedSocket = null;
                m_acceptTimeout = acceptTimeout;
            }

        }

        private class ConnectCallbackParams
        {
            private Socket m_connectingSocket;
            private ManualResetEvent m_mre;
            private bool m_aborted;

            public Socket ConnectingSocket
            {
                get { return m_connectingSocket; }
            }

            public ManualResetEvent Mre
            {
                get { return m_mre; }
            }

            public bool Aborted
            {
                get { return m_aborted; }
                set { m_aborted = value; }
            }

            public ConnectCallbackParams(Socket connectingSocket, ManualResetEvent mre)
            {
                this.m_connectingSocket = connectingSocket;
                this.m_mre = mre;
                this.m_aborted = false;
            }
        }

        private class AcceptCallbackParams
        {
            private Socket m_listeningSocket;
            private Socket m_acceptedSocket;
            private ManualResetEvent m_mre;
            private bool m_aborted;

            public Socket ListeningSocket
            {
                get { return m_listeningSocket; }
            }

            public Socket AcceptedSocket
            {
                get { return m_acceptedSocket; }
                set { m_acceptedSocket = value; }
            }

            public ManualResetEvent Mre
            {
                get { return m_mre; }
            }

            public bool Aborted
            {
                get { return m_aborted; }
                set { m_aborted = value; }
            }

            public AcceptCallbackParams(Socket listeningSocket, ManualResetEvent mre)
            {
                this.m_listeningSocket = listeningSocket;
                this.m_acceptedSocket = null;
                this.m_mre = mre;
                this.m_aborted = false;
            }
        }
    

    }
}
