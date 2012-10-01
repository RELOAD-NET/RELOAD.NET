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

//#define IETF83_ENROLL

/* There are two valid CSR bodys possible. The binary DER format and the 
 * PEM format, which is base64 between 
   -----BEGIN CERTIFICATE REQUEST-----
 * and 
 * -----END CERTIFICATE REQUEST-----
 *
 * note Thomas: Is that true? It looks like DER is standard
 */

#define ENROLL_USE_DER_FORMAT
//#define IMSI_AUTHENTIFICATION

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using TSystems.RELOAD.Utils;

using Microsoft.Ccr.Core;

#if COMPACT_FRAMEWORK
    using PocketDnDns;
    using PocketDnDns.Query;
    using PocketDnDns.Records;
#else
    using System.Net.NetworkInformation;
    using DnDns;
    using DnDns.Query;
    using DnDns.Records;
#endif

using SBSSLCommon;
using SBServer;
using SBUtils;
using SBX509;
using SBCustomCertStorage;
using SBClient;
using SBPKCS10;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;


namespace TSystems.RELOAD.Enroll
{
  public class ReloadConfigResolve
  {

        //for enrollment over https without a trusted (selfsigned) certificate
    private static bool OnCheckSSLCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
            return true;
        }

        /* RELOAD BASE 07, 10.2, pg.117 */
        /* DNS SRV for given Overlay name, if no config server URL is provided out of band */

        private bool m_fEnrollmentserverAvailable = false;

    private string configuration_url;
    private string enrollment_url;
        private ReloadConfig m_ReloadConfig;

    public ReloadConfigResolve(ReloadConfig reloadConfig)
    {
            m_ReloadConfig = reloadConfig;

            SBUtils.Unit.SetLicenseKey(ReloadGlobals.SBB_LICENSE_SBB7_KEY);
            SBUtils.Unit.SetLicenseKey(ReloadGlobals.SBB_LICENSE_PKI7_KEY);
        }

    public string EnrollmentUrl
    {
            get { return enrollment_url; }
        }

    public bool EnrollmentserverAvailable
    {
            get { return m_fEnrollmentserverAvailable; }
        }

    public string ResolveNaptr(string e164_Number)
    {
            /* Need to know about DNS servers available */
      try
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Resolving NAPTR for {0}", e164_Number));

                string e164_NumberNaptr = "";

        for (int i = e164_Number.Length - 1; i > 0; i--)
        {
                    if (e164_Number[i] == ' ' || e164_Number[i] == '+')
                        continue;
                    e164_NumberNaptr += e164_Number[i];
                    e164_NumberNaptr += ".";
                }
                e164_NumberNaptr += "e164.arpa";


                List<string> s_dnsServerAddrList = new List<string>();

                if (!s_dnsServerAddrList.Contains(ReloadGlobals.DNS_Address))
                    s_dnsServerAddrList.Add(ReloadGlobals.DNS_Address);
                //s_dnsServerAddrList.Add("141.39.41.73");

        foreach (string s_dnsServerAddr in s_dnsServerAddrList)
        {
                    DnsQueryRequest dnsQuery = new DnsQueryRequest();

                    //string e164_NumberNaptr = "2.2.2.2.2.2.2.1.7.1.9.4.e164.arpa";
                    //answer: ...d.u.E2U+sip$!^.*$!sip:+491712222222@mp2psip.org!.
#if COMPACT_FRAMEWORK
                    DnsQueryResponse dnsResponse = dnsQuery.Resolve(s_dnsServerAddr, e164_NumberNaptr, PocketDnDns.Enums.NsType.MAPTR, PocketDnDns.Enums.NsClass.INET, ProtocolType.Udp);
#else
                    DnsQueryResponse dnsResponse = dnsQuery.Resolve(s_dnsServerAddr, e164_NumberNaptr, DnDns.Enums.NsType.MAPTR, DnDns.Enums.NsClass.INET, ProtocolType.Udp);
#endif
                    if (dnsResponse != null)
            foreach (IDnsRecord record in dnsResponse.Answers)
            {
                            string[] lines = System.Text.RegularExpressions.Regex.Split(record.Answer, "!");

                            if (lines != null && lines.Length > 2)
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("DNS NAPTR for {0} returned: {1}", e164_Number, lines[2]));

                            return lines[2];
                        }
                }
            }
      catch (Exception ex)
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("DNS NAPTR for {0} failed: {1}", e164_Number, ex.Message));
            }
            return null;

        }

    public string ResolveConfigurationServer(string OverlayName)
    {
            if (ReloadGlobals.ForceLocalConfig)
                return null;

            /* Need to know about DNS servers available */
      try
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Resolving Configuration server...");

                List<string> s_dnsServerAddrList = new List<string>();

        if (ReloadGlobals.DNS_Address != null && ReloadGlobals.DNS_Address.Length != 0)
                if (!s_dnsServerAddrList.Contains(ReloadGlobals.DNS_Address))
                    s_dnsServerAddrList.Add(ReloadGlobals.DNS_Address);

        foreach (string s_dnsServerAddr in s_dnsServerAddrList)
        {

                    DnsQueryRequest dnsQuery = new DnsQueryRequest();

#if COMPACT_FRAMEWORK
                    DnsQueryResponse dnsResponse = dnsQuery.Resolve(s_dnsServerAddr, String.Format("_p2psip_enroll._tcp.{0}", OverlayName), PocketDnDns.Enums.NsType.SRV, PocketDnDns.Enums.NsClass.INET, ProtocolType.Udp);
#else

#if IETF83_ENROLL
                    DnsQueryResponse dnsResponse = dnsQuery.Resolve(s_dnsServerAddr, String.Format("_p2psip-enroll._tcp.{0}", OverlayName), DnDns.Enums.NsType.SRV, DnDns.Enums.NsClass.INET, ProtocolType.Udp);  //--IETF
#else
                    DnsQueryResponse dnsResponse = dnsQuery.Resolve(s_dnsServerAddr, String.Format("_reload-config._tcp.{0}", OverlayName), DnDns.Enums.NsType.SRV, DnDns.Enums.NsClass.INET, ProtocolType.Udp);  //--joscha
#endif

#endif
                    if (dnsResponse != null)
            foreach (IDnsRecord record in dnsResponse.Answers)
            {
                            string[] lines = System.Text.RegularExpressions.Regex.Split(record.Answer, "\r\n");
                            //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("DNS SRV lookup returned {0}", record.Answer));

              foreach (string line in lines)
              {
                if (line.StartsWith("HostName"))
                {
                                    string[] lines2 = System.Text.RegularExpressions.Regex.Split(line, " ");
                                    string enrollment_url = lines2[1].TrimEnd('.');

                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("DNS SRV lookup returned: https://{0}", enrollment_url));

                                    return String.Format("https://{0}/.well-known/reload-config", enrollment_url);
                                }
                            }
                        }
                }
            }
      catch (Exception ex)
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("DNS SRV for p2psip_enroll failed: {0}", ex.Message));
            }
            return null;
        }

    public TextReader GetConfigDocument()
    {
            TextReader tr_xml = null;

      if (!ReloadGlobals.ForceLocalConfig)
      {
        /* load static settings of enrollment server, in this case dynamic resolution is skipped */
        configuration_url = ReloadGlobals.ConfigurationServer;

        /* Determine Configuration server */
        if (configuration_url == "" || configuration_url == null) {
                int iRetries = 3;

          for (int i = 0; i < iRetries; i++)
          {
            configuration_url = new ReloadConfigResolve(m_ReloadConfig).ResolveConfigurationServer(m_ReloadConfig.OverlayName);

            if (configuration_url != null)
            {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TLS, String.Format("Configuration server URL as of DNS SRV: '{0}'", configuration_url));
                        break;
                    }
                }
        }

                HttpWebResponse httpWebesponse = null;

        if (configuration_url == null)
        {
          configuration_url = String.Format("https://{0}/.well-known/p2psip-enroll", m_ReloadConfig.OverlayName);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("DNS SRV failed, set configuration server URL to {0}.", configuration_url));
                }

        try
        {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Calling configuration server: {0}", configuration_url));
                    //Create a Web-Request to an URL
          HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(configuration_url);
                    httpWebRequest.Timeout = ReloadGlobals.WEB_REQUEST_TIMEOUT;
                    if(ReloadGlobals.IgnoreSSLErrors)
                        httpWebRequest.AuthenticationLevel = AuthenticationLevel.None;

                    if (m_ReloadConfig.DontCheckSSLCert == true)
                        ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback(OnCheckSSLCert);

                    //Send Web-Request and receive a Web-Response
                    httpWebesponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    m_fEnrollmentserverAvailable = true;
                }
        catch (WebException ex)
        {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Configuration server not available: " + ex.Message + "using local configuration");
                }

                //Translate data from the Web-Response to a string
        if (m_fEnrollmentserverAvailable)
        {
                    Stream dataStream = httpWebesponse.GetResponseStream();
                    tr_xml = new StreamReader(dataStream, Encoding.UTF8);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Successfully downloaded Configuration document");
                }
            }

            // if Configuration file retrieval failed, then read from local file
      if (tr_xml == null)
      {
#if COMPACT_FRAMEWORK
                // Check for OsVersion to remove URI prefix if it is not WindowsCE.
                string basex = Assembly.GetExecutingAssembly().GetName().CodeBase.ToString();
                string applicationDirectory = Path.GetDirectoryName(basex);
                tr_xml = new StreamReader(applicationDirectory + @"\\reload_enroll_config.xml");
#else
                tr_xml = new StreamReader(@"reload_enroll_config.xml");
#endif
            }

            return tr_xml;
        }

    public void ReadConfig()
    {
            TextReader tr_xml = (TextReader)GetConfigDocument();

      if (tr_xml != null)
      {
                //string s = tr_xml.ReadToEnd();
                XmlSerializer serializer = new XmlSerializer(typeof(overlayelement));
                m_ReloadConfig.Document = new ReloadOverlayConfiguration((overlayelement)serializer.Deserialize(tr_xml));

        try
        {

                    var p2psipConfig = m_ReloadConfig.Document.Overlay;

                    if (p2psipConfig.configuration.maxmessagesizeSpecified)
                        ReloadGlobals.MAX_PACKET_BUFFER_SIZE = (int)m_ReloadConfig.Document.Overlay.configuration.maxmessagesize;

                    if (p2psipConfig.configuration.reportingurl != null)
                        m_ReloadConfig.ReportURL = m_ReloadConfig.Document.Overlay.configuration.reportingurl;

          if (p2psipConfig.configuration.enrollmentserver != null)
            enrollment_url = p2psipConfig.configuration.enrollmentserver[0];

          foreach (kindblock block in p2psipConfig.configuration.requiredkinds)
          {
            if (block.kind.name != null)
            {
              if (block.kind.name.ToUpper() == "SIP-REGISTRATION")
                ReloadGlobals.SIP_REGISTRATION_DATA_MODEL = ReloadGlobals.DataModelFromString(block.kind.datamodel);
              else if (block.kind.name.ToUpper() == "CERTIFICATE_BY_NODE")
                ReloadGlobals.CERTIFICATE_BY_NODE_DATA_MODEL = ReloadGlobals.DataModelFromString(block.kind.datamodel);
              else if (block.kind.name.ToUpper() == "CERTIFICATE_BY_USER")
                ReloadGlobals.CERTIFICATE_BY_USER_DATA_MODEL = ReloadGlobals.DataModelFromString(block.kind.datamodel);
                            }
                            }
                    string rootcert = p2psipConfig.configuration.rootcert;

          if (rootcert != null && rootcert.Length > 0)
          {
                        //remove all whitespaces and trailing new lines!!
                        rootcert = rootcert.TrimStart('\n');
                        rootcert = rootcert.TrimEnd('\n');
                        rootcert = rootcert.Replace("  ", "");
                        m_ReloadConfig.CACertificate = new TElX509Certificate();
                        byte[] buffer = new System.Text.ASCIIEncoding().GetBytes(rootcert);
                        m_ReloadConfig.CACertificate.LoadFromBuffer(buffer);
                    }

                    if (p2psipConfig.configuration.landmarks != null &&
              p2psipConfig.configuration.landmarks.landmarkhost.Length > 0)
          {
                        var landmark = p2psipConfig.configuration.landmarks.landmarkhost[0].address;
                    }
                }
        catch (Exception ex)
        {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "ReadConfig: " + ex.Message);
                }
            }
        }

    public void SimpleNodeIdRequest()
    {
      try
      {

        if (EnrollmentserverAvailable)
        {
                    HttpWebRequest httpWebPost;

                    /* get node id */
                    if (m_ReloadConfig.IMSI != null && m_ReloadConfig.IMSI != "")
                        httpWebPost = (HttpWebRequest)WebRequest.Create(new Uri(EnrollmentUrl + "?type=imsi&IMSI=" + m_ReloadConfig.IMSI));
                    else
                        httpWebPost = (HttpWebRequest)WebRequest.Create(new Uri(EnrollmentUrl + "?type=vnode"));

                    /* As of RELOAD draft, use POST */
                    httpWebPost.Method = "POST";
                    httpWebPost.Timeout = ReloadGlobals.WEB_REQUEST_TIMEOUT;

                    HttpWebResponse httpPostResponse = null;
                    //Send Web-Request and receive a Web-Response
                    httpPostResponse = (HttpWebResponse)httpWebPost.GetResponse();
                    //Translate data from the Web-Response to a string
                    Stream dataStream2 = httpPostResponse.GetResponseStream();

                    TextReader tr_xml2 = new StreamReader(dataStream2, Encoding.UTF8);
                    string response = tr_xml2.ReadToEnd();

                    string[] words = response.Split(':', ',', '/', '@');

          if (m_ReloadConfig.IMSI != null && m_ReloadConfig.IMSI != "")
          {
                        m_ReloadConfig.E64_Number = words[1];

                        ReloadConfigResolve res = new ReloadConfigResolve(m_ReloadConfig);
                        m_ReloadConfig.SipUri = res.ResolveNaptr(m_ReloadConfig.E64_Number);

            if (m_ReloadConfig.SipUri == null)
            {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, "DNS Enum fallback to sip uri analysis");
                            m_ReloadConfig.SipUri = m_ReloadConfig.E64_Number;
                            m_ReloadConfig.SipUri = m_ReloadConfig.SipUri.TrimStart(' ');
                            m_ReloadConfig.SipUri = m_ReloadConfig.SipUri.Replace(" ", "");
                            m_ReloadConfig.SipUri = m_ReloadConfig.SipUri + "@" + m_ReloadConfig.OverlayName;
                        }

                        m_ReloadConfig.LocalNodeID = new NodeId(HexStringConverter.ToByteArray(words[7]));

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Enrollment Server assigned: NodeId = '{0}' SipUri = '{1}' ", m_ReloadConfig.LocalNodeID, m_ReloadConfig.SipUri));
                    }
          else
          {
                        m_ReloadConfig.LocalNodeID = new NodeId(HexStringConverter.ToByteArray(words[4]));
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Enrollment Server assigned: NodeId = '{0}'", m_ReloadConfig.LocalNodeID));
                    }
                }
            }
      catch (Exception ex)
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SimpleNodeIdRequest: " + ex.Message);
            }
        }

    public void EnrollmentProcedure()
    {
            m_ReloadConfig.ReloadLocalCertStorage = new TElMemoryCertStorage();
            m_ReloadConfig.ReloadLocalCertStorage.Clear();

            if (EnrollmentUrl != "")
        if (CertificateSigningRequest(EnrollmentUrl) == false)
        {
          if (m_ReloadConfig.CertName.Length > 0)
          {
                        FileStream fs = new FileStream(m_ReloadConfig.CertName, FileMode.Open, FileAccess.Read);
                        m_ReloadConfig.ReloadLocalCertStorage.LoadFromStreamPFX(fs, m_ReloadConfig.CertPassword, (int)fs.Length);
                    }
                }

      try
      {

                m_ReloadConfig.MyCertificate = m_ReloadConfig.ReloadLocalCertStorage.get_Certificates(0);

                if (m_ReloadConfig.MyCertificate == null)
                    throw new System.Exception("Got no certificate!");

                if (m_ReloadConfig.MyCertificate.Extensions.SubjectAlternativeName.Content.Count < 1)
                    throw new System.Exception("Invalid certificate");

                /* RELOAD BASE 07, pg. 112 */
        try
        {
                    ReloadGlobals.SelfSignPermitted = m_ReloadConfig.Document.Overlay.configuration.selfsignedpermitted.Value;
                }
                catch { };
        if (m_ReloadConfig.MyCertificate.SelfSigned)
        {
                    if (!ReloadGlobals.SelfSignPermitted)
                        throw new System.Exception("Found self signed certificate, but self signing is not allowed by config");
                }

                string rfc822Name = null;
                m_ReloadConfig.LocalNodeID = ReloadGlobals.retrieveNodeIDfromCertificate(
                  m_ReloadConfig.MyCertificate, ref rfc822Name);

        if (rfc822Name != null)
        {
          if (m_ReloadConfig.IMSI != null && m_ReloadConfig.IMSI != "" && m_ReloadConfig.IMSI != "VNODE")
          {
                        string[] rfc822NameSplit = rfc822Name.Split(':', ',', '/', '@');

                        m_ReloadConfig.E64_Number = rfc822NameSplit[0];
                        m_ReloadConfig.SipUri = "sip:" + rfc822Name;
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                          String.Format("Enrollment Server assigned: NodeId = '{0}' SipUri = '{1}' ", m_ReloadConfig.LocalNodeID, m_ReloadConfig.SipUri));
                    }
                    else
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Enrollment Server assigned: NodeId = '{0}'", m_ReloadConfig.LocalNodeID));
                }
                System.Diagnostics.Debug.Assert(m_ReloadConfig.LocalNodeID != null && m_ReloadConfig.LocalNodeID != m_ReloadConfig.LocalNodeID.Max() && m_ReloadConfig.LocalNodeID != m_ReloadConfig.LocalNodeID.Min());
            }
      catch (Exception ex)
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "CertificateSigningRequest: " + ex.Message);
            }
        }


        /*   The certificate request protocol is performed over HTTPS.  The
           request is an HTTP POST with the following properties:

           o  If authentication is required, there is a URL parameter of
              "password" and "username" containing the user's name and password
              in the clear (hence the need for HTTPS)
           o  The body is of content type "application/pkcs10", as defined in
              [RFC2311].
           o  The Accept header contains the type "application/pkix-cert",
              indicating the type that is expected in the response.
         */
        /// <summary>
        /// Used to generate certificate request
        /// </summary>
    public bool CertificateSigningRequest(string enrollment_url)
    {
            TElCertificateRequest FRequest = null;
            string sLocalCertFilename;

            sLocalCertFilename = m_ReloadConfig.IMSI == "" ? "VNODE_" + m_ReloadConfig.ListenPort.ToString() : m_ReloadConfig.IMSI;

#if COMPACT_FRAMEWORK
             // Check for OsVersion to remove URI prefix if it is not WindowsCE.
            string basex = Assembly.GetExecutingAssembly().GetName().CodeBase.ToString();
            string applicationDirectory = Path.GetDirectoryName(basex);

      string cert_file = applicationDirectory + "\\" + sLocalCertFilename + ".csr";
            string privateKey_file = applicationDirectory +  "\\" + sLocalCertFilename + ".key";
#else
#if ENROLL_USE_DER_FORMAT      
  string cert_file = sLocalCertFilename + ".der";
#else
      string cert_file = sLocalCertFilename + ".pem";
#endif
      string privateKey_file = sLocalCertFilename + ".key";
#endif
      byte[] byteCSR = null;
            byte[] privateKey = null;

      try
      {
        byteCSR = File.ReadAllBytes(cert_file);
                privateKey = File.ReadAllBytes(privateKey_file);
            }
      catch
      {
            }

      if (byteCSR == null || byteCSR.Length == 0)
      {
        try
        {
                    if (m_ReloadConfig.Logger != null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("Generate new Certificate Signing Request, please wait.."));
#if COMPACT_FRAMEWORK
                    if (m_ReloadConfig.Logger != null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("This is likely to crash on a mobile device. Use PC client to generate a key pair!"));
#endif
                    FRequest = new TElCertificateRequest(null);
                    FRequest.Subject.Count = 6;
                    for (int i = 0; i <= 5; i++) FRequest.Subject.set_Tags(i, SBASN1Tree.Unit.SB_ASN1_PRINTABLESTRING);

          /* private certificate configuration */  
          
                    FRequest.Subject.set_OIDs(0, SBUtils.Unit.SB_CERT_OID_COUNTRY);
                    FRequest.Subject.set_Values(0, SBUtils.Unit.BytesOfString("DE"));
                    FRequest.Subject.set_OIDs(1, SBUtils.Unit.SB_CERT_OID_STATE_OR_PROVINCE);
                    FRequest.Subject.set_Values(1, SBUtils.Unit.BytesOfString("Hessen"));
                    //FRequest.Subject.set_Values(1, SBUtils.Unit.BytesOfString("Hamburg"));
                    FRequest.Subject.set_OIDs(2, SBUtils.Unit.SB_CERT_OID_LOCALITY);
                    FRequest.Subject.set_Values(2, SBUtils.Unit.BytesOfString("Darmstadt"));
                    //FRequest.Subject.set_Values(2, SBUtils.Unit.BytesOfString("Hamburg"));
                    FRequest.Subject.set_OIDs(3, SBUtils.Unit.SB_CERT_OID_ORGANIZATION);
                    FRequest.Subject.set_Values(3, SBUtils.Unit.BytesOfString("T-Systems MP2P SIP"));
                    //FRequest.Subject.set_Values(3, SBUtils.Unit.BytesOfString("HAW-Hamburg MP2P SIP"));
                    FRequest.Subject.set_OIDs(4, SBUtils.Unit.SB_CERT_OID_ORGANIZATION_UNIT);
                    FRequest.Subject.set_Values(4, SBUtils.Unit.BytesOfString("R&D"));
                    FRequest.Subject.set_OIDs(5, SBUtils.Unit.SB_CERT_OID_COMMON_NAME);


#if IMSI_AUTHENTIFICATION
          FRequest.Subject.set_Values(5, SBUtils.Unit.BytesOfString(m_ReloadConfig.IMSI == "" ? "VNODE" : "IMSI:" + m_ReloadConfig.IMSI));
#else
          FRequest.Subject.set_Values(5, SBUtils.Unit.BytesOfString("thomas"));
#endif
                    FRequest.Generate(SBUtils.Unit.SB_CERT_ALGORITHM_ID_RSA_ENCRYPTION,
                                      2048,
                                      SBUtils.Unit.SB_CERT_ALGORITHM_SHA1_RSA_ENCRYPTION);

#if ENROLL_USE_DER_FORMAT
          FRequest.SaveToBuffer(out byteCSR);
#else
          FRequest.SaveToBufferPEM(out byteCSR);
#endif
                    FRequest.GetPrivateKey(out privateKey);

                    //save Certificate Signing Request and private Key to disk
          File.WriteAllBytes(cert_file, byteCSR);
                    File.WriteAllBytes(privateKey_file, privateKey);

                    if (m_ReloadConfig.Logger != null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("... ready."));
                }
        catch (Exception ex)
        {
                    if (m_ReloadConfig.Logger != null)
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                            String.Format("Generation of CSR failed {0}", ex.ToString()));
                }
            }

      try
      {
        if (enrollment_url != null)
        {
                    HttpWebRequest httpWebPost;

                    /* get node id and sip url */
#if IETF83_ENROLL
                    httpWebPost = (HttpWebRequest)WebRequest.Create(new Uri("https://implementers.org/enrollment?username=Joscha&password=password&count=1"));
#else
                    httpWebPost = (HttpWebRequest)WebRequest.Create(new Uri(enrollment_url));
#endif
                    /* As of RELOAD draft, use POST */
                    httpWebPost.Method = "POST";
                    httpWebPost.Accept = "application/pkix-cert";
                    httpWebPost.Timeout = ReloadGlobals.WEB_REQUEST_TIMEOUT;
                    httpWebPost.ProtocolVersion = HttpVersion.Version10;
                    httpWebPost.UserAgent = "T-Systems RELOAD MDI Appl 1.0";

#if ENROLL_USE_DER_FORMAT
        /*
          this is a sample post request of Marcs Testformular at
          https://reloadnet-reload.implementers.org/enrollment 
          -----------------------------265001916915724
          Content-Disposition: form-data; name="username" Thomas
          -----------------------------265001916915724
          Content-Disposition: form-data; name="password" **********
          -----------------------------265001916915724
          Content-Disposition: form-data; name="nodeids" 1
          -----------------------------265001916915724
          Content-Disposition: form-data; name="csr"; filename="blob"
          Content-Type: application/pkcs10
                     */

          string username = "test";
          string password = "test";
          //string username = "anonymous";
          //string password = "test";

          string boundary = "----------------------------" + DateTime.Now.Ticks.ToString("x");

          httpWebPost.ContentType = "multipart/form-data; boundary=" + boundary;

          byte[] boundaryclosebytes = System.Text.Encoding.ASCII.GetBytes("\r\n--" + boundary + "--\r\n");

          string formdataTemplate = "\r\n--"+ boundary +
              "\r\nContent-Disposition: form-data; name=\"username\"\r\n\r\n{0}\r\n--" + boundary +
              "\r\nContent-Disposition: form-data; name=\"password\"\r\n\r\n{1}\r\n--" + boundary +
              "\r\nContent-Disposition: form-data; name=\"nodeids\"\r\n\r\n1";

          string formitem = string.Format(formdataTemplate, username, password);
          byte[] formitembytes = System.Text.Encoding.UTF8.GetBytes(formitem);

          Stream memStream = new System.IO.MemoryStream();
          BinaryWriter writer = new BinaryWriter(memStream);

          writer.Write(formitembytes);
          formitem = "\r\n--" + boundary + "\r\nContent-Disposition: form-data; name=\"csr\"; filename=\"blob\"\r\nContent-Type: application/pkcs10\r\n\r\n";
          formitembytes = System.Text.Encoding.UTF8.GetBytes(formitem);
          writer.Write(formitembytes);
          writer.Write(byteCSR);
          writer.Write(boundaryclosebytes);
 
          httpWebPost.ContentLength = memStream.Length;

          Stream requestStream = httpWebPost.GetRequestStream();

          memStream.Position = 0;
          memStream.CopyTo(requestStream);

          using (Stream file = File.OpenWrite("C:\\Windows\\Temp\\test.dat"))
          {
            memStream.Position = 0;
            memStream.CopyTo(file);
          }

                    writer.Close();

#else
          httpWebPost.ContentType = "application/pkcs10";

                    StreamWriter writer = new StreamWriter(httpWebPost.GetRequestStream());
          string str_pemCSR = System.Text.Encoding.ASCII.GetString(byteCSR, 0, byteCSR.Length);
                    writer.Write(str_pemCSR);
                    writer.Close();

          httpWebPost.ContentLength = byteCSR.Length;
#endif
                    HttpWebResponse httpPostResponse = null;
                    //Send Web-Request and receive a Web-Response
          try
          {
                        httpPostResponse = (HttpWebResponse)httpWebPost.GetResponse();
                    }
          catch (WebException ex)
          {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "CSR returns:" + ex.Message);
                    }

          if (httpPostResponse != null)
          {

                    TElX509Certificate cert = new TElX509Certificate();

#if ENROLL_USE_DER_FORMAT
            cert.LoadFromBuffer(ReloadGlobals.ConvertNonSeekableStreamToByteArray(httpPostResponse.GetResponseStream()));
#else
            cert.LoadFromBufferPEM(ReloadGlobals.ConvertNonSeekableStreamToByteArray(httpPostResponse.GetResponseStream()), "");
#endif
            if (privateKey != null)
            {
                        cert.LoadKeyFromBuffer(privateKey);
                        m_ReloadConfig.ReloadLocalCertStorage.Add(cert, true); //true -> copy private key

              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Successfully received certificate, Issuer: {0}", cert.IssuerName.CommonName));
                    return true;
                }
            }
        }
      }
      catch (Exception ex)
      {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("CSR failed {0}", ex.ToString()));
            }
            return false;
        }
    }
}
