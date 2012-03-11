/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012 Thomas Kluge <t.kluge@gmx.de> 
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
* Last edited by: Alex <alexander.knauf@gmail.com>
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections;
using System.Collections.Generic;
//using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Net;
using System.IO;
using System.Net.Sockets;
using Microsoft.Win32;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Storage;

#if COMPACT_FRAMEWORK
    using Newtonsoft.Json;
#else
    using System.Web.Script.Serialization;
#endif

using SBX509;
using SBCustomCertStorage;
using System.Security.Cryptography;


namespace TSystems.RELOAD {

  public class ReloadConfig {

    private volatile Machine m_machine = null;

    public Machine ThisMachine {
      get { return m_machine; }
    }

    private ReloadOverlayConfiguration m_ReloadOverlayConfiguration = null;

    public ReloadOverlayConfiguration Document {
      get { return m_ReloadOverlayConfiguration; }
      set { m_ReloadOverlayConfiguration = value; }
    }

    public volatile NodeId LocalNodeID = null;
    public volatile String OverlayName = "";
    public volatile bool DontCheckSSLCert = false;   //enrollment server ssl cert
    public volatile Node LocalNode = null;
    public volatile Node AdmittingPeer = null;
    /// <summary>
    /// Local certificates and configuration stuff. 
    /// </summary>
    private TElMemoryCertStorage m_reload_local_cert_storage = null;

    public TElMemoryCertStorage ReloadLocalCertStorage {
      get { return m_reload_local_cert_storage; }
      set { m_reload_local_cert_storage = value; }
    }
    public volatile TElX509Certificate MyCertificate = null;
    public volatile TElX509Certificate CACertificate = null;
    public volatile int ListenPort = 6084;
    public int MyCoordinate = 0; // Mok for Demo
    public bool IsFocus = false; // Mok for Demo
    public NodeId MyFocus = null; // Mok for Demo
    public volatile bool IsBootstrap = false;
    public volatile bool IamClient = true; // false => Full RELOAD Peer; true => RELOAD Client
    public volatile string IMSI = "";
    public volatile string SipUri = "";
    public volatile string E64_Number = "";
    public volatile string CertName = "";
    public volatile string CertPassword = "";
    public volatile string TabPage = "";
    public volatile string ReportURL = "";    // for different ReportingUrls per instance

    public enum RELOAD_State {
      invalid = 0,
      Init = 1,
      Configured = 2,
      PreJoin = 3,    //final state for client
      Joining = 4,
      Joined = 5,
      Leave = 6,
      Shutdown = 7,
      Exit = 8 // accept no messages anymore
    }

    public volatile RELOAD_State State = RELOAD_State.invalid;

    public DateTime StartTime = DateTime.Now;
    public DateTime LastJoinedTime = DateTime.MinValue;
    // A value to measure joining time
    public DateTime StartJoining = DateTime.Now;
    public DateTime StartStore = DateTime.Now;
    public DateTime EndStore = DateTime.Now;
    public DateTime StartFetchAttach = DateTime.Now;
    public DateTime SendFetchAttach = DateTime.Now;

    // used for measurement of compact framework 
    public DateTime StartJoinMobile = DateTime.Now;
    public DateTime EndJoinMobile = DateTime.Now;
    public DateTime StartStoreMobile = DateTime.Now;
    public DateTime EndStoreMobile = DateTime.Now;

    public string hans = "";
    public DateTime ConnEstStart;
    public DateTime ConnEstEnd;

    /* global ID allows receivers to disambiguate transactions */
    public UInt64 TransactionID = BitConverter.ToUInt64(System.Guid.NewGuid().ToByteArray(), 0);

    volatile Queue m_CommandQueue = null;

    public Queue CommandQueue {
      get { return m_CommandQueue; }
      set { m_CommandQueue = value; }
    }

    private IAccessController m_AccessController;
    /// <summary>
    /// Returns the access controller that validates request or
    /// stored data.
    /// </summary>
    public IAccessController AccessController {
      get { return m_AccessController; }
      set { m_AccessController = value; }
    }

    volatile List<StoreKindData> m_GatheringList = null;

    public List<StoreKindData> GatheringList {

      get { return m_GatheringList; }
      set { m_GatheringList = value; }
    }

    private volatile Dispatcher m_Dispatcher = null;

    public Dispatcher Dispatcher {
      get {
        if (State == ReloadConfig.RELOAD_State.Exit) {
          return null;
        }
        return m_Dispatcher;
      }
      set { m_Dispatcher = value; }
    }
    private volatile DispatcherQueue m_DispatcherQueue = null;

    public DispatcherQueue DispatcherQueue {
      get {
        if (State == ReloadConfig.RELOAD_State.Exit) {
          return null;
        }
        return m_DispatcherQueue;

      }
      set { m_DispatcherQueue = value; }
    }

    private Statistics m_Statistics = null;

    public Statistics Statistics {
      get { return m_Statistics; }
      set { m_Statistics = value; }
    }
	
    public ReloadConfig() {
    }

    public ReloadConfig(Machine machine) {
      m_machine = machine;
      m_CommandQueue = new Queue();
      m_Dispatcher = new Dispatcher(0, ThreadPriority.Normal, true, "Dispatcher");
      m_DispatcherQueue = new DispatcherQueue("MainDispatcher", m_Dispatcher);
      m_Statistics = new Statistics(this);
    }

    // Declare a delegate that takes a single string parameter
    // and has no return type.
    public delegate void LogHandler(ReloadGlobals.TRACEFLAGS scope, string message);
    public LogHandler Logger = null;
  }

  public class ReloadOverlayConfiguration {
    private overlayelement overlay;
    public overlayelement Overlay {
      get { return overlay; }
    }

    /* Wraps enrollment overlay definition from XML file */
    public ReloadOverlayConfiguration(overlayelement overlay) {
      this.overlay = overlay;
    }
  }

  public static class HexStringConverter {
    public static byte[] ToByteArray(String HexString) {
      int NumberChars = HexString.Length;
      byte[] bytes = new byte[NumberChars / 2];
      for (int i = 0; i < NumberChars; i += 2) {
        bytes[i / 2] = Convert.ToByte(HexString.Substring(i, 2), 16);
      }
      return bytes;
    }
  }

  /// <summary>
  /// Singleton functions 
  /// </summary>
  public class ReloadGlobals {
    /* 2^127           170141183460469231731687303715884105728  */
    /* 2^128           340282366920938463463374607431768211456  */
    /* 2^159  730750818665451459101842416358141509827966271488  */
    /* 2^160 1461501637330902918203684832716283019655932542976  */
    public static bool SimpleNodeId = false;
    public static bool Client = false;
    public static bool TLS = true;
    public static SignatureAlgorithm SignatureAlg = SignatureAlgorithm.rsa;
    public static TSystems.RELOAD.Topology.HashAlgorithm HashAlg = TSystems.RELOAD.Topology.HashAlgorithm.sha256;
    public static bool Framing = true;
    public static bool IgnoreSSLErrors = true;
    public static bool IPv6_Enabled = false;
    /* 9.7.4.3 For many overlays, 16 finger table entries will be enough */
    public static int FINGER_TABLE_ENTRIES = 4;
    public static int RetransmissionTime = 3000;
    /* This additional value was added as the first tests turned out that in conguestion 
     * phases 3 seconds as suggested by the ROLOAD draft are simply not enough time, so 
     * we run into many unnessacary retransmisstions and transmission failures */
    public static int MaxTimeToSendPacket = 10000;//6000;
    public static int MaxRetransmissions = 4;
    public static bool TLS_PASSTHROUGH = false;
    public static bool AllowPrivateIP = false;

    public static UInt32 SIP_REGISTRATION_KIND_ID = 1234;
    public static UInt32 DISCO_REGISTRATION_KIND_ID = 4321;
    public static UInt32 ACCESS_LIST_KIND_ID = 3210;
	public static UInt32 REDIR_KIND_ID = 104;
	
    public static bool SelfSignPermitted = false;
    public static readonly DateTime StartOfEpoch = new DateTime(1970, 1, 1);    

    public enum DataModel {
      INVALID = 0,
      SINGLE_VALUE = 1,
      ARRAY = 2,
      DICTIONARY = 3
    }
    public static DataModel SIP_REGISTRATION_DATA_MODEL = DataModel.SINGLE_VALUE;
    public static UInt32 SIP_REGISTRATION_DATA_LIFETIME = 86400; //in seconds which is 24h
    public static DataModel DISCO_REGISTRATION_DATA_MODEL = DataModel.DICTIONARY;
    public static UInt32 DISCO_REGISTRATION_LIFETIME = 86400;
    public static DataModel ACCESS_LIST_DATA_MODEL = DataModel.ARRAY;
    public static UInt32 ACCESS_LIST_LIFETIME = 86400;
    public static DataModel CERTIFICATE_BY_NODE_DATA_MODEL = DataModel.ARRAY;
    public static bool ReportEnabled = false;
    public static bool IsVirtualServer = false;
    public static bool ReportIncludeConnections = false;
    public static bool ReportIncludeFingers = false;
    public static bool ReportIncludeStatistic = false;
    public static bool ReportIncludeTopology = true;
    public static bool AutoExe = false;  
    public static string ReportURL = "";    
    public static string DNS_Address = "141.22.26.233";
    public static string EnrollmentServer = "";
    /* Set this value to false, to set a fixed IP for enrollment server 
     */
    public static bool UseDNS = false;
    /* set FixedDNS to true if the DNS ist configured manually, in that case a Webrequest
     * to a URL resolved by DNS must be translated to IP-Address before!
     */
    public static bool FixedDNS = false;
    public static string RegKeyIPC = "Software\\T-Systems\\RELOAD";
    public static bool ForceLocalConfig = false;
    public static bool DocumentAutoScroll = true;

    /* Max nr of digits for keys */
    public static int NODE_ID_DIGITS = 16;           /* To carry 128 bits maximum */
    public static int MAX_RESOURCE_ID_DIGITS = 20;   /* To carry 160 bits maximum */
    public static int DICTIONARY_KEY_LENGTH = 16;
    public static NodeId WildcardNodeId = new NodeId(HexStringConverter.ToByteArray("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"));
   
    public static string SBB_LICENSE_SBB8_KEY = "Insert here your licence key";
    public static string SBB_LICENSE_PKI8_KEY = "Insert here your licence key";

    public static int WEB_REQUEST_TIMEOUT = 30000;
    public static int MAX_TLS_SEND_QUEUE_SIZE = 4;
    public static int RELOAD_SERVER_PORT = 6084;
    public static int MAX_INCOMING_PENDING_CONNECTIONS = 10;

    public static int MAX_PACKET_BUFFER_SIZE = 2048;

    /* It is often the case, that on high churn multiple packets will be received on one socket. 
       this will handle this 
     */
    public static int MAX_PACKETS_PER_RECEIVE_LOOP = 4;
    public static int MAX_VIA_LIST_ENTRIES = 33; // 15 is normal

    //in seconds
    public static int CHORD_PING_INTERVAL = 300;
    public static int CHORD_RELOAD_PING_INTERVAL = 3600;
    public static int CHORD_UPDATE_INTERVAL = 600;

    public static int REPORTING_PERIOD = 5000;
    public static int MAINTENANCE_PERIOD = 15000;
    public static bool fMaintenance = true;
    public static int SUCCESSOR_CACHE_SIZE = 3;

    public static int BootstrapPort = 0;
    public static string BootstrapHost = "";
    public static string HostName = Dns.GetHostName();

    /* An 8 bit field indicating the number of iterations, or hops, a
       message can experience before it is discarded.  */
    public static Byte TTL = 100;
    /* The version of the RELOAD protocol being used.  This
       document describes version 0.1, with a value of 0x01. */
    public static Byte RELOAD_VERSION = 0x01;

    public static String OverlayName = "t-reload.realmv6.org";
    // public static String OverlayName = "mp2psip.org";
    /* The 32 bit checksum/hash of the overlay being used */
    public static UInt32 OverlayHash = 0x00000001;

    /*enable fragmentation of outgoing packages (only implemented for TLS)*/
    public static bool FRAGMENTATION = false;
    /* hardcoded fragment_size, not compliant with draft (only implemented for TLS)*/
    public static uint FRAGMENT_SIZE = 1000;

    #region Trace


    public static bool TimeStamps = true;
    public static TRACEFLAGS TRACELEVEL = (TRACEFLAGS)0x0000FF0F;
    public enum TRACEFLAGS {
      T_INFO = 1,
      T_ERROR = 2,
      T_TRANSPORT = 4,
      T_DATASTORE = 8,
      T_LOOKUP = 0x10,
      T_TLS = 0x20,
      T_FH = 0x40,
      T_SOCKET = 0x80,
      T_KEEPALIVE = 0x100,
      T_RELOAD = 0x200,
      T_TOPO = 0x400,
      T_FORWARDING = 0x800,
      T_USAGE = 0x1000,
      T_WARNING = 0x2000,
      T_MEASURE = 0x4000,
      T_REDIR = 0x8000,
      T_FRAGMENTATION = 0xC000,
      T_BUG = 0x10000,
      T_ALL = 0xFFFF,
    }

    public static void TRACE(TRACEFLAGS scope, string message) {
#if !COMPACT_FRAMEWORK
      lock ("trace")
#endif
 {
        if ((scope & TRACELEVEL) != 0) {
#if !COMPACT_FRAMEWORK
          switch (scope) {
            case TRACEFLAGS.T_DATASTORE:
              Console.ForegroundColor = ConsoleColor.Gray;
              break;
            case TRACEFLAGS.T_ERROR:
              Console.ForegroundColor = ConsoleColor.Red;
              break;
            case TRACEFLAGS.T_FH:
              Console.ForegroundColor = ConsoleColor.Gray;
              break;
            case TRACEFLAGS.T_INFO:
              Console.ForegroundColor = ConsoleColor.Gray;
              break;
            case TRACEFLAGS.T_LOOKUP:
              Console.ForegroundColor = ConsoleColor.Gray;
              break;
            case TRACEFLAGS.T_SOCKET:
              Console.ForegroundColor = ConsoleColor.DarkGray;
              break;
            case TRACEFLAGS.T_TLS:
              Console.ForegroundColor = ConsoleColor.DarkGray;
              break;
            case TRACEFLAGS.T_TRANSPORT:
              Console.ForegroundColor = ConsoleColor.DarkGray;
              break;
            case TRACEFLAGS.T_TOPO:
              Console.ForegroundColor = ConsoleColor.Blue;
              break;
            case TRACEFLAGS.T_FORWARDING:
              Console.ForegroundColor = ConsoleColor.DarkCyan;
              break;
            case TRACEFLAGS.T_RELOAD:
              Console.ForegroundColor = ConsoleColor.DarkBlue;
              break;
            case TRACEFLAGS.T_USAGE:
              Console.ForegroundColor = ConsoleColor.DarkGreen;
              break;
            case TRACEFLAGS.T_WARNING:
              Console.ForegroundColor = ConsoleColor.Magenta;
              break;
            case TRACEFLAGS.T_MEASURE:
              Console.ForegroundColor = ConsoleColor.Cyan;
              break;
            case TRACEFLAGS.T_BUG:
              Console.ForegroundColor = ConsoleColor.DarkYellow;
              break;
            case TRACEFLAGS.T_KEEPALIVE:
              Console.ForegroundColor = ConsoleColor.DarkGray;
              Console.Write('.');
              return;
          }
#endif

#if COMPACT_FRAMEWORK
                    Console.WriteLine(message);
#else
          String line = String.Format("{0} [{1}]: {2}", DateTime.Now.ToString("HH:mm:ss.fff"), System.Threading.Thread.CurrentThread.ManagedThreadId, message);

          System.Diagnostics.Trace.WriteLine(line);
          //Console.WriteLine(line);

          bool success = true;

          while (!success) {
            try {
              // Open and read the file.
              StreamWriter w = File.AppendText("log.txt");

              //TKDODO                    w.WriteLine(String.Format("{0} {1}", m_ReloadConfig.LocalNodeID, line));
              w.Close();

              success = true;
            }
            catch (IOException ex) {
              int hr = (int)ex.GetType().GetProperty("HResult",

              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)

              .GetValue(ex, null);

              if (hr == -2147024864) {
                continue;
              }
              else
                throw; // Not an exception we recognize; rethrow.
            }
          }
#endif
        }
      }
    }

    /// <summary>
    /// Hex-Dumps the <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns></returns>
    public static string HexDump(byte[] buffer) {
      StringBuilder sb = new StringBuilder();
      int currentOffset = 0;
      string ASCII = "";
      foreach (byte b in buffer) {
        if ((currentOffset % 16) == 0) {
          sb.Append(String.Format("{0:X8}: ", currentOffset));
        }
        sb.Append(String.Format("{0:X2} ", b));
        ASCII += ((b >= 0x20 && b <= 0x7F) ? Convert.ToChar(b).ToString() : ".");
        currentOffset++;
        if ((currentOffset % 16) == 0) {
          sb.Append(ASCII + "\r\n");
          ASCII = "";
        }
      }
      for (int i = 16; i > ASCII.Length; i--)
        sb.Append("   ");
      sb.Append(ASCII);
      return sb.ToString();
    }

    public static string PlainHexDump(Byte[] buffer) {
      string tmp = "";
      for (int i = 0; i < buffer.Length; i++)
        tmp += String.Format("{0:X02} ", buffer[i]);
      return tmp;
    }

    #endregion

    /// <summary>
    /// SHA-1 hash
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns></returns>
    public static NodeId GetHash(byte[] data) {
      SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider();
      byte[] bytes = sha1.ComputeHash(data);
      return new NodeId(bytes);
    }
#if false 
        /// <summary>
        /// SHA-256 hash
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        public static NodeId GetHash256(byte[] data)
        {
            SHA256Managed sha256 = new SHA256Managed();
            byte[] bytes = sha256.ComputeHash(data);
            return new NodeId(bytes);
        }
#endif
    public static byte[] HexToBytes(string hexString) {
      if (hexString == null)
        return null;
      if (hexString.Length % 2 == 1)
        return null;
      byte[] data = new byte[hexString.Length / 2];
      for (int i = 0; i < data.Length; i++)
        data[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
      return data;
    }

    public static string JSONSerialize(object o) {
#if COMPACT_FRAMEWORK
            return JsonConvert.SerializeObject(o);
#else
      JavaScriptSerializer serializer = new JavaScriptSerializer();
      return serializer.Serialize(o);
#endif
    }

    public static bool IsPrivateIP(IPAddress myIPAddress) {
      if (myIPAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
        byte[] ipBytes = myIPAddress.GetAddressBytes();

        // 10.0.0.0/24 
        if (ipBytes[0] == 10 && ipBytes[1] != 33) {
          return true;
        }
        // 172.16.0.0/16
        else if (ipBytes[0] == 172 && ipBytes[1] == 16) {
          return true;
        }
        // 192.168.0.0/16
        else if (ipBytes[0] == 192 && ipBytes[1] == 168) {
          return true;
        }
        // 169.254.0.0/16
        else if (ipBytes[0] == 169 && ipBytes[1] == 254) {
          return true;
        }
      }
      if (myIPAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) {
        if (myIPAddress.ToString() == "::1") {
          return true;
        }
      }
      return false;
    }

    public static IPAddress IPAddressFromHost(ReloadConfig rc, string host) {
      IPAddress Address = null;
      try {
        Address = IPAddress.Parse(host);
      }
      catch {
        IPAddress[] IPAddresses = Dns.GetHostEntry(host).AddressList;
        foreach (IPAddress ip in IPAddresses) {
          if (!(!ReloadGlobals.IPv6_Enabled && (ip.AddressFamily == AddressFamily.InterNetworkV6)))
            if (IsPrivateIP(ip) && !AllowPrivateIP) {
              rc.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Interface with private IP {0} ignored", ip.ToString()));
            }
            else {
              Address = ip;
              rc.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("IP {0}", ip.ToString()));
              break;
            }

        }
      }
      return Address;
    }

    /// <summary>
    /// Table lookup for pow 2 (finger calculations)
    /// </summary>
    public static NodeId[] BigIntPow2Array = new NodeId[] {
     
            new NodeId(new byte[] {0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 }),

            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 }),
            new NodeId(new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }),
        };

    #region Serialization/Deserialization support

    internal static void Endian<T>(T msg, ref byte[] data) {
      int idx = 0;
      FieldInfo[] fia = msg.GetType().GetFields();
      foreach (FieldInfo fi in fia) {

        int sz = 0;
        if (fi.FieldType.BaseType.UnderlyingSystemType.Name == "Enum")
          sz = 1; /* This is a RELOAD dictated MUST */
        else
          sz = Marshal.SizeOf(Type.GetType(fi.FieldType.FullName));

        if (fi.FieldType.UnderlyingSystemType.Name == "Int64" ||
            fi.FieldType.UnderlyingSystemType.Name == "UInt64" ||
            fi.FieldType.UnderlyingSystemType.Name == "Int32" ||
            fi.FieldType.UnderlyingSystemType.Name == "UInt32" ||
            fi.FieldType.UnderlyingSystemType.Name == "Int16" ||
            fi.FieldType.UnderlyingSystemType.Name == "UInt16") {
          Array.Reverse(data, idx, sz);
        }
        idx += sz;
      }
    }

    public static byte[] SerializeMessage<T>(T msg) where T : struct {
      int objsize = Marshal.SizeOf(typeof(T));
      byte[] data = new byte[objsize];
      IntPtr buff = Marshal.AllocHGlobal(objsize);
      Marshal.StructureToPtr(msg, buff, true);
      Marshal.Copy(buff, data, 0, objsize);
      Marshal.FreeHGlobal(buff);
      Endian<T>(msg, ref data);
      return data;
    }

    public static T DeserializeMsg<T>(byte[] data) where T : struct {
      int objsize = Marshal.SizeOf(typeof(T));
      IntPtr buff = Marshal.AllocHGlobal(objsize);
      Endian<T>(new T(), ref data);
      Marshal.Copy(data, 0, buff, objsize);
      T retStruct = (T)Marshal.PtrToStructure(buff, typeof(T));
      Marshal.FreeHGlobal(buff);
      return retStruct;
    }
    #endregion

    public static UInt32 WriteOpaqueValue(BinaryWriter writer, Byte[] value, UInt64 uiMaxValue) {
      UInt32 length = 0;

      if (value != null) {
        if (uiMaxValue <= 0xFF) {
          writer.Write((Byte)value.Length);
          length += 1;
        }
        else if (uiMaxValue <= 0xFFFF) {
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)value.Length));
          length += 2;
        }

        else if (uiMaxValue <= 0xFFFFFFFF) {
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)value.Length));
          length += 4;
        }

        writer.Write(value);
        length += (UInt32)value.Length;
      }
      else {
        writer.Write((Byte)0);
        length = length + 1;
      }
      return length;
    }

    public static void StoreRegAnswer(string answer) {
#if COMPACT_FRAMEWORK
            RegistryKey regKeyIPC = Registry.LocalMachine.CreateSubKey(RegKeyIPC);
#else
      RegistryKey regKeyIPC = Registry.CurrentUser.CreateSubKey(RegKeyIPC);
#endif
      if (regKeyIPC != null) {
        regKeyIPC.SetValue("Answer", answer, RegistryValueKind.String);
      }
    }

    public static byte[] ConvertNonSeekableStreamToByteArray(Stream NonSeekableStream) {
      MemoryStream ms = new MemoryStream();
      byte[] buffer = new byte[1024];
      int bytes;
      while ((bytes = NonSeekableStream.Read(buffer, 0, buffer.Length)) > 0) {
        ms.Write(buffer, 0, bytes);
      }
      byte[] output = ms.ToArray();
      return output;
    }

    public static void PrintException(ReloadConfig rc, Exception ex) {
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("****** LastChanceHandler ******"));
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("ExceptionType: {0}", ex.GetType().Name));
#if !COMPACT_FRAMEWORK
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("HelpLine: {0}", ex.HelpLink));
#endif
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Message: {0}", ex.Message));
#if !COMPACT_FRAMEWORK
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Source: {0}", ex.Source));
#endif
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("StackTrace: {0}", ex.StackTrace));
#if !COMPACT_FRAMEWORK
      rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("TargetSite: {0}", ex.TargetSite));
#endif
      string indent = "   ";
      Exception ie = ex;
      while (!((ie.InnerException == null))) {
        ie = ie.InnerException;
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "****** Inner Exception ******"));
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "ExceptionType: {0}", ie.GetType().Name));
#if !COMPACT_FRAMEWORK
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "HelpLine: {0}", ie.HelpLink));
#endif
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "Message: {0}", ie.Message));
#if !COMPACT_FRAMEWORK
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "Source: {0}", ie.Source));
#endif
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "StackTrace: {0}", ie.StackTrace));
#if !COMPACT_FRAMEWORK
        rc.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format(indent + "TargetSite: {0}", ie.TargetSite));
#endif
        indent += "         ";
      }
    }

    /// <summary>
    /// Retrieve node_id from certificate from certificate.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns></returns>
    public static NodeId retrieveNodeIDfromCertificate(TElX509Certificate certificate, ref string rfc822Name) {
      if (!certificate.SelfSigned) {
        string nodeID = null;   /* Accept one nodeID only */
        for (int i = 0; i < certificate.Extensions.SubjectAlternativeName.Content.Count; i++) {
          if (certificate.Extensions.SubjectAlternativeName.Content.get_Names(i).RFC822Name != null)
            rfc822Name = certificate.Extensions.SubjectAlternativeName.Content.get_Names(i).RFC822Name;
          if (certificate.Extensions.SubjectAlternativeName.Content.get_Names(i).UniformResourceIdentifier != null)
            nodeID = certificate.Extensions.SubjectAlternativeName.Content.get_Names(i).UniformResourceIdentifier;
        }

        if (nodeID == null)
          throw new System.Exception("Invalid certificate, node_id missing");

        string[] nodeIDSplit = nodeID.Split(':', ',', '/', '@');

        return new NodeId(HexStringConverter.ToByteArray(nodeIDSplit[3]));
      }
      else
      /* RELOAD BASE 07, 10.3.1, pg. 118 */
      /* NodeID is hash(publicKey) in case self-certificate */ {
        byte[] shaHash = null;
        byte[] publicKey;
        certificate.GetPublicKeyBlob(out publicKey);
        /* TODO: Handle non-self signed certificates */
        //if (m_ReloadOverlayConfiguration.Overlay.configuration.selfsignedpermitted.digest.ToLower().Trim() == "sha1")
        {
          SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider();
          shaHash = sha1.ComputeHash(publicKey);
        }
        /* TKTODO no sha256 definition for Compact Framework
         *          else {
                        SHA256Managed sha256 = new SHA256Managed();
                        shaHash = sha256.ComputeHash(publicKey);
                    }
         **/
        /* Truncate lower significant bytes in order to get a 128 bit NodeID */
        Array.Resize<byte>(ref shaHash, 16);
        return new NodeId(shaHash);
      }
    }
  }

#if COMPACT_FRAMEWORK
   class File
   {
        // these nice wrappers are missing in Compact Framework
        public static byte[] ReadAllBytes(string path)
        {
           byte[] buffer;

           using (FileStream fs = new FileStream(path, FileMode.Open,
           FileAccess.Read, FileShare.Read))
           {
               int offset = 0;
               int count = (int)fs.Length;
               buffer = new byte[count];
               while (count > 0)
               {
                   int bytesRead = fs.Read(buffer, offset, count);
                   offset += bytesRead;
                   count -= bytesRead;
               }
           }

           return buffer;
        }

        public static void WriteAllBytes(String path, byte[] bytes)
        { 
            if (bytes == null)
                throw new ArgumentNullException("bytes"); 

            using(FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                fs.Write(bytes, 0, bytes.Length); 
        }
    }
#endif
}
