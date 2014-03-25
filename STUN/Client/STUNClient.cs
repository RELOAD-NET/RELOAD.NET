using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;


namespace STUN
{
    public class STUNClient
    {
        // Konfiguration
        private const int RTO = 500;    // Retransmission TimeOut UDP
        private const int RC = 7;       // maximale Anzahl der Sendeversuche
        private const int RM = 16;      // Anzahl RTO's die nach letztem (RC) Sendeversuch gewartet wird
        private const int TI = 39500;   // Retransmission TimeOut TCP

        public enum TransportProtocol
        {
            UDP = 1,
            TCP = 2
        }

        public enum IPVersion
        {
            IPv4 = 1,
            IPv6 = 2
        }


        private String m_IP;
        private int m_Port;
        private TransportProtocol m_TransportProtocol;



        // Konstruktor
        public STUNClient(String HostnameOrIP, int Port, TransportProtocol TransportProtocol, IPVersion IPVersion)
        {
            m_Port = Port;
            m_TransportProtocol = TransportProtocol;

            IPAddress dummy;
            if (IPAddress.TryParse(HostnameOrIP, out dummy))
                m_IP = HostnameOrIP;
            else
            {
                try
                {
                    IPHostEntry hostInfo = Dns.GetHostEntry(HostnameOrIP);

                    // IPv4
                    if (IPVersion == STUNClient.IPVersion.IPv4)
                    {
                        // alle Einträge nach IPv4 Adresse durchsuchen
                        foreach (IPAddress ip in hostInfo.AddressList)
                        {
                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                m_IP = ip.ToString();
                                break;
                            }
                        }

                        // falls keine IPv4 Adresse gefunden wurde
                        if (m_IP == null)
                            throw new Exception("Zu dem angegebenen Host konnte keine IPv4 Adresse gefunden werden");
                    }

                    // andernfalls IPv6
                    else
                    {
                        // alle Einträge nach IPv6 Adresse durchsuchen
                        foreach (IPAddress ip in hostInfo.AddressList)
                        {
                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                            {
                                m_IP = ip.ToString();
                                break;
                            }
                        }

                        // falls keine IPv6 Adresse gefunden wurde
                        if (m_IP == null)
                            throw new Exception("Zu dem angegebenen Host konnte keine IPv6 Adresse gefunden werden");
                    }

                }
                catch (Exception e)
                {
                    throw e;
                }
            }


        }




        // Funktionen

        private static STUNMessage SendOverUDP(String IP, int Port, STUNMessage StunMessage)
        {
            IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Parse(IP), Port);
            IPEndPoint localEndpoint = new IPEndPoint(IPAddress.Any, 0);

            UdpClient udpClient = null;

            // IPv4 oder IPv6 Adresse?
            if (remoteEndpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                udpClient = new UdpClient();
            else if (remoteEndpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                udpClient = new UdpClient(System.Net.Sockets.AddressFamily.InterNetworkV6);

            // Nachricht in Byte Array wandeln
            Byte[] message_buffer = StunMessage.ToByteArray();



            // initialen TimeOut Wert festlegen
            udpClient.Client.ReceiveTimeout = RTO;

            // Buffer für Empfang der Antwort
            Byte[] response_buffer = null;

            //Stopwatch stopwatch = new Stopwatch();
            //stopwatch.Start();

            // MessageType
            int type = (int)StunMessage.StunMessageType;

            // Indication?
            // 8.Bit(Maske 0x100) gelöscht? und 4.Bit(Maske 0x10) gesetzt?
            if (((type & 0x100) == 0) && ((type & 0x10) != 0))
            {
                // Indication nur ein Sendeversuch
                //Console.WriteLine("Indication");

                try
                {
                    // Versuchen zu senden
                    udpClient.Send(message_buffer, message_buffer.Length, remoteEndpoint);

                    // auf Antwort warten   => AUF INDICATION KOMMT KEINE ANTWORT !!!
                    //response_buffer = udpClient.Receive(ref localEndpoint);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    throw e;
                }

            }
            else
            {
                // Request oder Response
                //Console.WriteLine("Request oder Response");

                // RC Versuche
                for (int versuch = 1; versuch <= RC; versuch++)
                {
                    try
                    {
                        // Versuchen zu senden
                        //Console.WriteLine("Sende bei ({0} ms)", stopwatch.ElapsedMilliseconds);
                        udpClient.Send(message_buffer, message_buffer.Length, remoteEndpoint);

                        // auf Antwort warten
                        response_buffer = udpClient.Receive(ref localEndpoint);

                        // wenn Antwort erhalten springe aus Sende-Schleife
                        break;
                    }
                    catch (SocketException e)
                    {
                        //stopwatch.Stop();
                        if (e.SocketErrorCode == SocketError.TimedOut)
                        {
                            // welcher Versuch ist gescheitert?
                            if (versuch < RC - 1)
                                udpClient.Client.ReceiveTimeout *= 2;
                            // wenn vorletzter Versuch gescheitert ist => letzten Versuch mit ReceiveTimeOut = RM * RTO
                            else if (versuch == RC - 1)
                                udpClient.Client.ReceiveTimeout = RM * RTO;
                            // letzter Versuch ebenfalls gescheitert
                            else if (versuch == RC)
                            {
                                //stopwatch.Stop();
                                //Console.WriteLine("Sendevorgang nach {0} Versuchen und {1} ms abgebrochen!", RC, stopwatch.ElapsedMilliseconds);
                            }
                        }

                        /*
                        if (e.SocketErrorCode == SocketError.NetworkUnreachable)       // trifft nicht ein
                        {
                            Console.WriteLine("unreachable");
                        }
                        */

                        else
                        {
                            Console.WriteLine(e.Message);
                            throw e;
                        }

                    }

                }

            }


            // UdpClient schließen
            udpClient.Close();

            // Nachricht erhalten?
            if (response_buffer != null)
            {
                STUNMessage response = STUNMessage.Parse(response_buffer);
                return response;
            }
            else
                return null;

        }


        // Methode nur für Clients gedacht, weil Client die TCP Verbindung öffnet
        private static STUNMessage SendOverTCP(String IP, int Port, STUNMessage StunMessage)
        {

            try
            {
                TcpClient tcpClient = new TcpClient(IP, Port);

                // TimeOut Wert festlegen
                tcpClient.Client.ReceiveTimeout = TI;

                // Nachricht in Byte Array wandeln
                Byte[] message_buffer = StunMessage.ToByteArray();

                // Netzwerkstream holen
                NetworkStream stream = tcpClient.GetStream();

                // Message senden 
                stream.Write(message_buffer, 0, message_buffer.Length);


                // zuerst Header (20 Byte) empfangen um Länge der Nachricht zu ermitteln
                Byte[] header = new Byte[20];
                stream.Read(header, 0, 20);

                // Länge parsen (3. und 4. Byte im Header)
                Int16 msg_length = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(header, 2));

                // Byte Array für Response
                Byte[] response = new Byte[20 + msg_length];

                // Header in Response kopieren
                Array.Copy(header, 0, response, 0, header.Length);

                // Rest der Nachricht empfangen
                int bytesRead = stream.Read(response, 20, msg_length);

                // alles eingetroffen?
                if (bytesRead != msg_length)
                    Console.WriteLine("Fehler beim empfangen!");

                // parsen
                STUNMessage stunResponse = STUNMessage.Parse(response);

                // aufräumen
                stream.Close();
                tcpClient.Close();

                return stunResponse;


            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw e;

            }

        }


        private static STUNMessage SendOverTCP(String IP, int Port, STUNMessage StunMessage, IPEndPoint localEndpoint)
        {

            try
            {
                TcpClient tcpClient = new TcpClient(localEndpoint);
                tcpClient.Connect(IP, Port);

                // TimeOut Wert festlegen
                tcpClient.Client.ReceiveTimeout = TI;

                // Nachricht in Byte Array wandeln
                Byte[] message_buffer = StunMessage.ToByteArray();

                // Netzwerkstream holen
                NetworkStream stream = tcpClient.GetStream();

                // Message senden 
                stream.Write(message_buffer, 0, message_buffer.Length);


                // zuerst Header (20 Byte) empfangen um Länge der Nachricht zu ermitteln
                Byte[] header = new Byte[20];
                stream.Read(header, 0, 20);

                // Länge parsen (3. und 4. Byte im Header)
                Int16 msg_length = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(header, 2));

                // Byte Array für Response
                Byte[] response = new Byte[20 + msg_length];

                // Header in Response kopieren
                Array.Copy(header, 0, response, 0, header.Length);

                // Rest der Nachricht empfangen
                int bytesRead = stream.Read(response, 20, msg_length);

                // alles eingetroffen?
                if (bytesRead != msg_length)
                    Console.WriteLine("Fehler beim empfangen!");

                // parsen
                STUNMessage stunResponse = STUNMessage.Parse(response);

                // aufräumen
                //Thread.Sleep(2000);
                stream.Close();
                tcpClient.Close();

                return stunResponse;


            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw e;

            }

        }


        // Messages erzeugen:
        private static STUNMessage FormSTUNBindingRequest()
        {
            STUNMessage stunBindingRequest = new STUNMessage(StunMessageType.BindingRequest);
            // TEST Fingerprint
            FingerprintAttribute fingerprint = FingerprintAttribute.CreateFingerprint();
            stunBindingRequest.AddAttribute(fingerprint);

            stunBindingRequest.Create();

            return stunBindingRequest;
        }

        private static STUNMessage FormSTUNIndication()
        {
            STUNMessage stunIndicationRequest = new STUNMessage(StunMessageType.BindingIndication);
            stunIndicationRequest.Create();

            return stunIndicationRequest;
        }

        private static STUNMessage FormSTUNIndication(String Software)
        {
            STUNMessage stunIndicationRequest = new STUNMessage(StunMessageType.BindingIndication);
            SoftwareAttribute software = SoftwareAttribute.CreateSoftware(Software);
            stunIndicationRequest.AddAttribute(software);
            stunIndicationRequest.Create();

            return stunIndicationRequest;
        }

        // Success und Error Responses werden nur vom Server erzeugt



        // Messages verarbeiten:

        // Requests werden nur vom Server verarbeitet

        // für Binding Methode werden keine Indications verarbeitet


        private static IPEndPoint ProcessSTUNBindingSuccessResponse(STUNMessage StunMessage)
        {
            // unknown comprehension-required attributes?
            //if(StunMessage.ContainsUnknownComprehensionRequiredAttributes)
            //          return false;

            // XOR Mapped Address vorhanden?
            for (int i = 0; i < StunMessage.AttributeList.Count; i++)
            {
                if (StunMessage.AttributeList[i].Type == STUNAttribute.StunAttributeType.XorMappedAddress)
                {
                    //return StunMessage.AttributeList[i].XorMappedAddress;
                    XorMappedAddressAttribute xmaa = (XorMappedAddressAttribute)StunMessage.AttributeList[i];
                    return xmaa.XorMappedAddress;

                }
            }

            // falls nicht Mapped Address vorhanden?
            for (int i = 0; i < StunMessage.AttributeList.Count; i++)
            {
                if (StunMessage.AttributeList[i].Type == STUNAttribute.StunAttributeType.MappedAddress)
                {
                    //return StunMessage.AttributeList[i].MappedAddress;
                    MappedAddressAttribute maa = (MappedAddressAttribute)StunMessage.AttributeList[i];
                    return maa.MappedAddress;
                }
            }

            // keins von beiden Attributen vorhanden ist vorhanden
            return null;
        }

        private static STUNErrorCode ProcessSTUNBindingErrorResponse(STUNMessage StunMessage)
        {
            // Error Code vorhanden?
            for (int i = 0; i < StunMessage.AttributeList.Count; i++)
            {
                if (StunMessage.AttributeList[i].Type == STUNAttribute.StunAttributeType.ErrorCode)
                {
                    //return StunMessage.AttributeList[i].ErrorCode;
                    ErrorCodeAttribute eca = (ErrorCodeAttribute)StunMessage.AttributeList[i];
                    return eca.ErrorCode;
                }
            }

            // kein Error Code Attribut
            return null;

        }


        public String GetPublicIP()
        {

            IPEndPoint localEndPoint = null;
            STUNErrorCode errorCode = null;

            STUNMessage bindingRequest = FormSTUNBindingRequest();

            STUNMessage response = null;

            // warte auf zugehörige Antwort
            do
            {
                // UDP ?
                if (m_TransportProtocol == TransportProtocol.UDP)
                    response = SendOverUDP(m_IP, m_Port, bindingRequest);

                // TCP ?
                else if (m_TransportProtocol == TransportProtocol.TCP)
                    response = SendOverTCP(m_IP, m_Port, bindingRequest);

            } while (!response.CompareTransactionIDs(bindingRequest));



            // Binding Success Response?
            if (response.StunMessageType == StunMessageType.BindingSuccessResponse)
            {
                // prüfe Response
                localEndPoint = ProcessSTUNBindingSuccessResponse(response);

                // kein (XOR)Mapped Address Attribut enthalten
                if (localEndPoint == null)
                    throw new Exception("Die Antwort vom Server enthält kein (XOR)Mapped Address Attribut!");

                // IP Adresse auslesen
                else
                    return localEndPoint.Address.ToString();


            }

            // Binding Error Response
            else if (response.StunMessageType == StunMessageType.BindingErrorResponse)
            {
                // prüfe Response
                errorCode = ProcessSTUNBindingErrorResponse(response);

                // kein Error Code Attribut enthalten
                if (errorCode == null)
                    throw new Exception("Die Antwort vom Server enthält kein Error Code Attribut!");

                // Error Code ausgeben
                else
                {
                    String error = "STUN Error Code " + errorCode.ErrorCode + ": " + errorCode.ReasonPhrase;
                    throw new Exception(error);
                }

            }

            // unbekannte Response
            else
                throw new Exception("Unbekannter Response Typ!");

        }


        public IPEndPoint GetPublicIPEndPoint()
        {

            IPEndPoint localEndPoint = null;
            STUNErrorCode errorCode = null;

            STUNMessage bindingRequest = FormSTUNBindingRequest();

            STUNMessage response = null;

            // warte auf zugehörige Antwort
            do
            {
                // UDP ?
                if (m_TransportProtocol == TransportProtocol.UDP)
                    response = SendOverUDP(m_IP, m_Port, bindingRequest);

                // TCP ?
                else if (m_TransportProtocol == TransportProtocol.TCP)
                    response = SendOverTCP(m_IP, m_Port, bindingRequest);

            } while (!response.CompareTransactionIDs(bindingRequest));



            // Binding Success Response?
            if (response.StunMessageType == StunMessageType.BindingSuccessResponse)
            {
                // prüfe Response
                localEndPoint = ProcessSTUNBindingSuccessResponse(response);

                // kein (XOR)Mapped Address Attribut enthalten
                if (localEndPoint == null)
                    throw new Exception("Die Antwort vom Server enthält kein (XOR)Mapped Address Attribut!");

                // Endpoint zurückgeben
                else
                    return localEndPoint;


            }

            // Binding Error Response
            else if (response.StunMessageType == StunMessageType.BindingErrorResponse)
            {
                // prüfe Response
                errorCode = ProcessSTUNBindingErrorResponse(response);

                // kein Error Code Attribut enthalten
                if (errorCode == null)
                    throw new Exception("Die Antwort vom Server enthält kein Error Code Attribut!");

                // Error Code ausgeben
                else
                {
                    String error = "STUN Error Code " + errorCode.ErrorCode + ": " + errorCode.ReasonPhrase;
                    throw new Exception(error);
                }

            }

            // unbekannte Response
            else
                throw new Exception("Unbekannter Response Typ!");

        }

        public IPEndPoint GetPublicIPEndPoint(IPEndPoint localEndpoint)
        {

            IPEndPoint localEndPoint = null;
            STUNErrorCode errorCode = null;

            STUNMessage bindingRequest = FormSTUNBindingRequest();

            STUNMessage response = null;

            // warte auf zugehörige Antwort
            do
            {
                // UDP ?
                if (m_TransportProtocol == TransportProtocol.UDP)
                    response = SendOverUDP(m_IP, m_Port, bindingRequest);

                // TCP ?
                else if (m_TransportProtocol == TransportProtocol.TCP)
                    response = SendOverTCP(m_IP, m_Port, bindingRequest, localEndpoint);

            } while (!response.CompareTransactionIDs(bindingRequest));



            // Binding Success Response?
            if (response.StunMessageType == StunMessageType.BindingSuccessResponse)
            {
                // prüfe Response
                localEndPoint = ProcessSTUNBindingSuccessResponse(response);

                // kein (XOR)Mapped Address Attribut enthalten
                if (localEndPoint == null)
                    throw new Exception("Die Antwort vom Server enthält kein (XOR)Mapped Address Attribut!");

                // Endpoint zurückgeben
                else
                    return localEndPoint;


            }

            // Binding Error Response
            else if (response.StunMessageType == StunMessageType.BindingErrorResponse)
            {
                // prüfe Response
                errorCode = ProcessSTUNBindingErrorResponse(response);

                // kein Error Code Attribut enthalten
                if (errorCode == null)
                    throw new Exception("Die Antwort vom Server enthält kein Error Code Attribut!");

                // Error Code ausgeben
                else
                {
                    String error = "STUN Error Code " + errorCode.ErrorCode + ": " + errorCode.ReasonPhrase;
                    throw new Exception(error);
                }

            }

            // unbekannte Response
            else
                throw new Exception("Unbekannter Response Typ!");

        }

    }
}
