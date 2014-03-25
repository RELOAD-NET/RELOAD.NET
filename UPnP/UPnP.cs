using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace UPNP
{
    public class UPnP
    {
        // Member und Eigenschaften
        private string m_DescriptionURL = null;
        private string m_ServiceURL = null;
        private string m_EventURL = null;

        public string DescriptionURL { get { return m_DescriptionURL; } }
        public string ServiceURL { get { return m_ServiceURL; } }
        public string EventURL { get { return m_EventURL; } }


        // Lokalisierung
        public bool Discover(IPAddress localIPAddress)
        {
            // Socket für UDP Pakete
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(localIPAddress, 0));
            socket.ReceiveTimeout = 3000;

            // SSDP Request String
            string ssdpRequestString =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +                                      // The field value specifies the value of "ssdp:discover" to be used for the search in SSDP
                "MX: 3\r\n" +                                                       // Maximum time (seconds) to wait for the M-SEARCH response
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";  // This specifies the Search Target to search for using M-SEARCH

            // SSDP Request
            byte[] ssdpRequest = Encoding.ASCII.GetBytes(ssdpRequestString);

            // Request geht an Multicast-Adresse 239.255.255.250:1900
            IPEndPoint ipEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            // Empfangspuffer
            byte[] buffer = new byte[4096];
            int length = 0;


            try
            {
                // UDP Paket absenden
                socket.SendTo(ssdpRequest, ipEndpoint);

                // auf Antwort(en) warten
                do
                {
                    length = socket.Receive(buffer);
                    string response = Encoding.ASCII.GetString(buffer, 0, length);

                    // Internet Gateway Device?
                    if (response.Contains("urn:schemas-upnp-org:device:InternetGatewayDevice:1"))
                    {
                        // Description URL extrahieren
                        int startLocationIndex = response.ToLower().IndexOf("location:");
                        response = response.Substring(startLocationIndex + "location:".Length);
                        int endLocationIndex = response.IndexOf("\r");
                        m_DescriptionURL = response.Substring(0, endLocationIndex).Trim();

                        if (GetServiceAndEventURL(m_DescriptionURL))
                            return true;
                    }
                }
                while (length > 0);

            }
            catch (Exception e)
            {
                if (e is SocketException)
                {
                    SocketException se = (SocketException)e;

                    if (se.SocketErrorCode == SocketError.TimedOut)
                        throw new Exception("No UPnP device found!");
                }

                throw e;                
            }

            return false;
        }


        // Externe IP ermitteln
        public IPAddress GetExternalIP()
        {
            // Discover() ausgeführt?
            if (string.IsNullOrEmpty(m_ServiceURL))
                throw new Exception("Discover() has not been called");

            try
            {
                // SOAP Request abschicken und Antwort als XML Dokument empfangen
                XmlDocument response = SOAPRequest(m_ServiceURL, 
                    "<u:GetExternalIPAddress xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" + 
                    "</u:GetExternalIPAddress>", 
                    "GetExternalIPAddress");

                // externe IP auslesen und zurückgeben
                string externalIP = response.SelectSingleNode("//NewExternalIPAddress/text()").Value;

                return IPAddress.Parse(externalIP);
            }
            catch (Exception e)
            {
                throw e;
            }

        }


        // Port Freigabe eintragen
        public bool AddPortMapping(ushort internalPort, ushort externalPort, ProtocolType protocolType, string description)
        {
            // Discover() ausgeführt?
            if (string.IsNullOrEmpty(m_ServiceURL))
                throw new Exception("Discover() has not been called");

            try
            {
                // lokale IPv4 Adresse bestimmen
                IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
                IPAddress internalClient = null;
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        internalClient = address;
                }

                // Request abschicken und Antwort empfangen
                XmlDocument response = SOAPRequest(m_ServiceURL,
                    "<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" + 
                    "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + externalPort.ToString() + "</NewExternalPort><NewProtocol>" + protocolType.ToString().ToUpper() + "</NewProtocol>" + 
                    "<NewInternalPort>" + internalPort.ToString() + "</NewInternalPort><NewInternalClient>" + internalClient.ToString() + 
                    "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>" + description + 
                    "</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration></u:AddPortMapping>", 
                    "AddPortMapping");

                return true;
            }
            catch (Exception e)
            {
                if (e is WebException)
                {
                    WebException we = (WebException)e;

                    HttpWebResponse wr = (HttpWebResponse)we.Response;

                    if (wr.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        throw new Exception("Changes of the security settings over UPnP are not allowed. Check your UPnP Router settings!");
                    }
                }
                
                throw e;
            }

        }


        // Port Freigabe löschen
        public bool DeletePortMapping(ushort externalPort, ProtocolType protocolType)
        {
            // Discover() ausgeführt?
            if (string.IsNullOrEmpty(m_ServiceURL))
                throw new Exception("Discover() has not been called");

            try
            {
                // Request abschicken und Antwort empfangen
                XmlDocument xdoc = SOAPRequest(m_ServiceURL,
                    "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" + 
                    "<NewRemoteHost></NewRemoteHost>" + "<NewExternalPort>" + externalPort.ToString() + "</NewExternalPort>" + 
                    "<NewProtocol>" + protocolType.ToString().ToUpper() + "</NewProtocol>" +
                    "</u:DeletePortMapping>", 
                    "DeletePortMapping");

                return true;
            }
            catch (Exception e)
            {
                throw e;
            }

        }


        // SOAP Request erstellen und abschicken
        private XmlDocument SOAPRequest(string serviceURL, string soapRequest, string action)
        {
            // Request zusammenbauen
            string requestString = "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            soapRequest +
            "</s:Body>" +
            "</s:Envelope>";

            byte[] request = Encoding.UTF8.GetBytes(requestString);

            try
            {
                // Request abschicken
                WebRequest webRequest = HttpWebRequest.Create(serviceURL);
                webRequest.Method = "POST";
                webRequest.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:WANIPConnection:1#" + action + "\"");
                webRequest.ContentType = "text/xml; charset=\"utf-8\"";
                webRequest.ContentLength = request.Length;
                webRequest.GetRequestStream().Write(request, 0, request.Length);

                // Response empfangen
                XmlDocument xmlResponse = new XmlDocument();
                WebResponse webResponse = webRequest.GetResponse();
                Stream responseStream = webResponse.GetResponseStream();
                xmlResponse.Load(responseStream);

                // und zurückgeben
                return xmlResponse;
            }
            catch (Exception e)
            {
                throw e;
            }
        }


        // Ermittle Service und Event URL
        private bool GetServiceAndEventURL(string descriptionURL)
        {

            XmlDocument description = new XmlDocument();
            WebRequest webRequest = WebRequest.Create(descriptionURL);

            try
            {
                // rufe Device Description ab
                description.Load(webRequest.GetResponse().GetResponseStream());

                XmlNamespaceManager xmlNSManager = new XmlNamespaceManager(description.NameTable);
                xmlNSManager.AddNamespace("ns", "urn:schemas-upnp-org:device-1-0");

                // Internet Gateway Device?
                XmlNode deviceTypeNode = description.SelectSingleNode("//ns:device/ns:deviceType/text()", xmlNSManager);
                if (!deviceTypeNode.Value.Contains("InternetGatewayDevice"))
                    return false;

                // suche Service URL
                XmlNode serviceURLNode = description.SelectSingleNode("//ns:service[ns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/ns:controlURL/text()", xmlNSManager);
                if (serviceURLNode == null)
                    return false;
                else
                    m_ServiceURL = BuildURL(descriptionURL, serviceURLNode.Value);

                // suche Event URL
                XmlNode eventURLNode = description.SelectSingleNode("//ns:service[ns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/ns:eventSubURL/text()", xmlNSManager);
                if (eventURLNode == null)
                    return false;
                else
                    m_EventURL = BuildURL(descriptionURL, eventURLNode.Value);

                return true;
            }
            catch (Exception e)
            {
                throw e;
            }

        }


        // Hilfsfunktion um URLs zusammenzufügen
        private string BuildURL(string baseUrl, string path)
        {
            int startBaseIndex = baseUrl.IndexOf("://");
            int endBaseIndex = baseUrl.IndexOf("/", startBaseIndex + "://".Length);

            return baseUrl.Substring(0, endBaseIndex) + path;
        }

    }
}
