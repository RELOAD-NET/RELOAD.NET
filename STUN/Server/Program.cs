using STUN;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {

            int listenPort = 3478;

            // wenn Port angegeben Standard Port überschreiben
            if (args.Length == 1)
                listenPort = int.Parse(args[0]);


            // localEndPoint IP Adresse eventuell anpassen an gleichen localEndPoint wie andere Socket wegen S-O
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, listenPort);

            TcpListener listener = new TcpListener(localEndPoint);
            listener.Start();

            Console.WriteLine("Server started on Port {0}.", listenPort);


            while (true)
            {
                TcpClient stunClient = listener.AcceptTcpClient();
                StunServer stunServer = new StunServer(stunClient);
            }

        }

        public class StunServer
        {
            private TcpClient m_client;

            public StunServer(TcpClient client)
            {
                m_client = client;
                Thread serverThread = new Thread(ServerMethod);
                serverThread.Start();
            }

            private void ServerMethod()
            {
                // get Network Stream
                NetworkStream ns = m_client.GetStream();

                while (IsSocketConnected(m_client.Client))
                {
                    // data available?
                    if (ns.DataAvailable)
                    {
                        // 1. Receive STUN Message
                        int bytesRead = 0;

                        // read first 20 bytes (header)
                        byte[] stunHeader = new byte[20];
                        int read = ns.Read(stunHeader, 0, stunHeader.Length);

                        // parse length of message (3rd and 4th byte)
                        ushort stunMsgLength = NetworkByteArray.ReadUInt16(stunHeader, 2);

                        // Byte Array for request
                        Byte[] stunRequest = new Byte[20 + stunMsgLength];

                        // copy header to request
                        Array.Copy(stunHeader, 0, stunRequest, 0, stunHeader.Length);

                        // is there more to read? if yes get the rest of the message
                        if (stunMsgLength > 0)
                            bytesRead = ns.Read(stunRequest, 20, stunMsgLength);

                        // received entire message?
                        if (bytesRead != stunMsgLength)
                            Console.WriteLine("Error receiving Stun Message!");

                        // parse message
                        STUNMessage request = STUNMessage.Parse(stunRequest);

                        // contains Fingerprint?
                        if (request.ContainsFingerprint())
                            // validate Fingerprint, in error case discard message
                            if (!request.ValidateFingerprint())
                                return;


                        // 2. Process STUN Message
                        STUNMessage response = null;

                        // Binding Request?
                        if (request.StunMessageType == StunMessageType.BindingRequest)
                        {
                            // public Endpoint of Client
                            IPEndPoint remoteEndPoint = (IPEndPoint)m_client.Client.RemoteEndPoint;

                            // TransactionID of Request also for Response
                            Byte[] reqTransID = request.TransactionID;
                            response = new STUNMessage(StunMessageType.BindingSuccessResponse, reqTransID);

                            // add Attributes and create message
                            XorMappedAddressAttribute xmaa = XorMappedAddressAttribute.CreateXorMappedAddress(response.TransactionID, remoteEndPoint.Address.ToString(), (ushort)remoteEndPoint.Port);
                            response.AddAttribute(xmaa);
                            MappedAddressAttribute maa = MappedAddressAttribute.CreateMappedAddress(remoteEndPoint.Address.ToString(), (ushort)remoteEndPoint.Port);
                            response.AddAttribute(maa);

                            response.Create();
                        }

                        else if (request.StunMessageType == StunMessageType.BindingIndication)
                        {
                            // No response is generated for an indication (RFC 5389, 7.3.2.)
                        }

                        byte[] stunResponse = response.ToByteArray();

                        ns.Write(stunResponse, 0, stunResponse.Length);
                        ns.Flush();
                    }

                    // no data available
                    else
                        Thread.Sleep(10);
                }

            }
        }

        private static bool IsSocketConnected(Socket client)
        {
            // http://msdn.microsoft.com/de-de/library/system.net.sockets.socket.connected(v=vs.110).aspx
            // This is how you can determine whether a socket is still connected.
            bool blockingState = client.Blocking;
            try
            {
                byte[] tmp = new byte[1];

                client.Blocking = false;
                client.Send(tmp, 0, 0);
                //Console.WriteLine("Connected!");
                return true;
            }
            catch (SocketException e)
            {
                // 10035 == WSAEWOULDBLOCK
                if (e.NativeErrorCode.Equals(10035))
                    //Console.WriteLine("Still Connected, but the Send would block");
                    return true;
                else
                {
                    //Console.WriteLine("Disconnected: error code {0}!", e.NativeErrorCode);
                    return false;
                }
            }
            finally
            {
                client.Blocking = blockingState;
            }


        }
    }
}
