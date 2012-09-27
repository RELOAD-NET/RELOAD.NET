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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using SBX509;
using System.Security.Cryptography;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Extension;

namespace TSystems.RELOAD.Transport {
  #region Messages

  public enum NodeState {
    unknown = 0,
    attaching = 1,
    attached = 2,
    updates_received = 3
  }

  public enum AttachOption {
    standard = 1,    // normal action
    forceupdate = 2, // set request update flag
    sendping = 4,    // request physical connection to destination
  }

  public enum PingOption {
    standard = 1,   // normal action
    direct = 2,     // direct connection to destination requested
    force = 4,      // ignore current state of node
    finger = 8,     // is a request by fix fingers
  }


  /* RELOAD Message Codes      */
  public enum RELOAD_MessageCode {
    Invalid = 0,
    Probe_Request = 1,
    Probe_Answer = 2,
    Attach_Request = 3,
    Attach_Answer = 4,
    Unused = 5,
    Unused2 = 6,
    Store_Request = 7,
    Store_Answer = 8,
    Fetch_Request = 9,
    Fetch_Answer = 10,
    Remove_Request = 11,
    Remove_Answer = 12,
    Find_Request = 13,
    Find_Answer = 14,
    Join_Request = 15,
    Join_Answer = 16,
    Leave_Request = 17,
    Leave_Answer = 18,
    Update_Request = 19,
    Update_Answer = 20,
    Route_Query_Request = 21,
    Route_Query_Answer = 22,
    Ping_Request = 23,
    Ping_Answer = 24,
    Stat_Request = 25,
    Stat_Answer = 26,
    App_Attach_Request = 29,
    App_Attach_Answer = 30,    //draft #7 states this a "attach_ans" what I think is a typo
    Error = 0xffff
  }

  /* RELOAD Error Codes      */
  public enum RELOAD_ErrorCode {
    invalid = 0,
    Unused = 1,
    Error_Forbidden = 2,
    Error_Not_Found = 3,
    Error_Request_Timeout = 4,
    Error_Generation_Counter_Too_Low = 5,
    Error_Incompatible_with_Overlay = 6,
    Error_Unsupported_Forwarding_Option = 7,
    Error_Data_Too_Large = 8,
    Error_Data_Too_Old = 9,
    Error_TTL_Exceeded = 10,
    Error_Message_Too_Large = 11,
    Error_Unknown_Kind = 12,
    Error_Unknown_Extension = 13
  }

  public enum ChordUpdateType {
    reserved = 0,
    peer_ready = 1,
    neighbors = 2,
    full = 3
  }

  public enum CertType {
    x509 = 0
  }

  public enum DestinationType {
    node = 1,
    resource = 2,
    compressed = 3
  }

  public enum SipRegistrationType {
    sip_registration_uri = 1,
    sip_registration_route = 2
  }

  /* A NodeId is a fixed-length 128-bit structure represented as a series
     of bytes, with the most significant byte first. 
   */

  public struct DestinationData {
    public NodeId node_id;
    public ResourceId ressource_id;
  }



  /// <summary>
  /// In response to a successful Store request the peer MUST return a
  /// StoreAns message containing a series of StoreKindResponse elements
  /// containing the current value of the generation counter for each
  /// Kind-ID, as well as a list of the peers where the data will be
  /// replicated by the node processing the request.
  /// see RELOAD base -12 p.90
  /// </summary>
  public struct StoreKindResponse {
    public UInt32 kind;
    public UInt64 generation_counter;
    public List<NodeId> replicas;
  }



  /// <summary>
  /// The FetchAns structure contains a series of FetchKindResponse
  /// structures.  There MUST be one FetchKindResponse element for each
  /// Kind-ID in the request.
  /// see RELOAD base -12 p.95
  /// </summary>
  public class FetchKindResponse {
    public UInt32 kind;
    public UInt64 generation;
    public List<StoredData> values;

    public FetchKindResponse() { }

    public FetchKindResponse(UInt32 kind, UInt64 generation,
      List<StoredData> values) {
      this.kind = kind;
      this.generation = generation;
      this.values = values;
    }

    public override string ToString() {

      string toString = "";

      foreach (StoredData storedData in values)
        toString += storedData.ToString() + "\n";

      return toString;
    }
  }

  public class Destination {
    public DestinationType type;
    //      public Byte length;
    public DestinationData destination_data;

    public Destination(NodeId id) {
      type = DestinationType.node;
      destination_data.node_id = id;
    }
    public Destination(ResourceId id) {
      type = DestinationType.resource;
      destination_data.ressource_id = id;
    }
    public override string ToString() {
      switch (type) {
        case DestinationType.node:
          if (destination_data.node_id != null)
            return "Node: " + destination_data.node_id.ToString();
          return "Node: null!!";
        case DestinationType.resource:
          return "Resource: " + destination_data.ressource_id.ToString();
        case DestinationType.compressed:
          return "Compressed: (unsupported)";
        default:
          return "<invalid>";
      }
    }

    public int GetNetLength() {
      int Length = 2;

      switch (type) {
        case DestinationType.node:
          Length += (ushort)ReloadGlobals.NODE_ID_DIGITS;
          break;
        case DestinationType.resource:
          Length += destination_data.ressource_id.Data.Length + 1;
          break;
      }
      return Length;
    }
  }

  // struct DestinationComp{
  //     public UInt16 compressed_id; /* top bit MUST be 1 */
  // }

  public enum ForwardingOptionsType {
    reservedForwarding = 0,
    directResponseForwarding = 1,
    destinationOverlay = 42,
    sourceOverlay = 43
  }

  public struct ForwardingOption {
    public ForwardingOptionsType fwo_type;
    public Byte flags;
    public UInt16 length;
    public Byte[] bytes;
    public static Byte FORWARD_CRITICAL = 0x01;
    public static Byte DESTINATION_CRITICAL = 0x02;
    public static Byte RESPONSE_COPY = 0x04;

    public static UInt32 FRAGMENTATION_IS_FRAGMENTED = 0x80000000;
    public static UInt32 FRAGMENTATION_LAST_SEGMENT = 0x40000000;
  }

  public struct ForwardingHeader {
    public UInt32 overlay;
    public UInt16 configuration_sequence;
    public Byte version;
    public Byte ttl;
    public UInt32 fragment;
    public UInt32 length;
    /*A unique 64 bit number that identifies this
      transaction and also allows receivers to disambiguate transactions
      which are otherwise identical.  Responses use the same Transaction
      ID as the request they correspond to.  Transaction IDs are also
      used for fragment reassembly.*/
    public UInt64 transaction_id;
    public UInt32 max_response_length;
    public UInt16 via_list_length;
    public UInt16 destination_list_length;
    public UInt16 options_length;
    public List<Destination> via_list;
    public List<Destination> destination_list;
    public List<ForwardingOption> fw_options;
  }

  /* Currently on rebuild
  public struct SecurityBlock {
    public NodeId OriginatorNodeID;
    public byte[] Cert;
  }
   **/

  /// <summary>
  /// The third part of a RELOAD message is the security block.  The
  /// security block is represented by a SecurityBlock structure
  /// 
  /// see base -19 p.52
  /// </summary>
  public class SecurityBlock {

    #region Properties
    /* Just for TCP test implementation */
    private NodeId compliantDummy;
    /* Helpers */
    private ReloadConfig m_ReloadConfig;
    private IAccessController m_AccessControl;

    private SignerIdentity signerId;

    public NodeId OriginatorNodeID {
      get {
        var ascii = Encoding.ASCII;
        string rfc822Name = null;
        var signerCert = new TElX509Certificate();
        var sha256 = new SHA256Managed();

        foreach (GenericCertificate cert in certificates) {
          byte[] hash = sha256.ComputeHash(cert.Certificate);
          var certHash = signature.Identity.Identity.CertificateHash;
          if (certHash.SequenceEqual(certHash))
          {
            signerCert.LoadFromBuffer(cert.Certificate);
            break;
          }
        }
        var originatorId = ReloadGlobals.retrieveNodeIDfromCertificate(
          signerCert, ref rfc822Name);

        return originatorId;
      }

      set { compliantDummy = value; }
    }

    private List<GenericCertificate> certificates;
    /// <summary>
    /// Returns all certificates carried by this message.
    /// </summary>
    public List<GenericCertificate> Certificates {
      get { return certificates; }
    }

    private Signature signature;
    public Signature Signature {
      get { return signature; }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor. Use it as receiver of a request.
    /// </summary>
    public SecurityBlock(ReloadConfig rc) {
      m_ReloadConfig = rc;
      m_AccessControl = rc.AccessController;
      certificates = new List<GenericCertificate>();
    }

    /// <summary>
    /// Creates a new Security Block for ordinary messages
    /// </summary>
    /// <param name="signerCert">X.509 PKC of the request originator</param>
    public SecurityBlock(ReloadConfig rc, SignerIdentity myIdentity) {
      m_ReloadConfig = rc;
      m_AccessControl = rc.AccessController;
      /* Add the certificate of signer */
      certificates = new List<GenericCertificate>();
      GenericCertificate myCert = m_AccessControl.GetPKC(myIdentity);
      certificates.Add(myCert);
      signerId = myIdentity;
    }

    /// <summary>
    /// Creates a new Security Block for data transporting messages
    /// </summary>
    /// <param name="signerCert">X.509 PKC of the request originator</param>
    /// <param name="certs">X.509 PKCs for validation data</param>
    public SecurityBlock(ReloadConfig rc, SignerIdentity myIdentity, List<byte[]> certs) {
      m_ReloadConfig = rc;
      m_AccessControl = rc.AccessController;
      /* Add the certificate of signer */
      certificates = new List<GenericCertificate>();
      GenericCertificate myCert = m_AccessControl.GetPKC(myIdentity);
      certificates.Add(myCert);
      /* Add all other PKCs */
      foreach (byte[] pkc in certs) {
        certificates.Add(new GenericCertificate(pkc));
      }
      signerId = myIdentity;
    }

    #endregion

    #region Public methods

    /// <summary>
    /// This method signs a message content contained within the stream.
    /// </summary>
    /// <param name="transId">The transaction Id used for signing</param>
    /// <param name="stream">The stream containing ONLY the content</param>
    public void SignMessage(UInt32 overlay, string transId, RELOAD_MessageBody body) {
      var ms = new MemoryStream();
      var br = new BinaryWriter(ms);
      body.Dump(br);
      ms.Position = 0;
      var reader = new StreamReader(ms);
      string msg = reader.ReadToEnd();
      signature = new Signature(overlay, transId, msg, signerId, m_ReloadConfig);
    }

    /// <summary>
    /// Dumps the security block to byte stream.
    /// </summary>
    /// <param name="write">The BinaryWriter</param>
    /// <returns>Total length in UInt16</returns>
    public UInt16 Dump(BinaryWriter writer) {
      var ascii = new ASCIIEncoding();
      var defEncode = Encoding.Default;
      long posBeforeCerts = writer.BaseStream.Position;
      /* Placeholder for length of certificates */
      writer.Write(IPAddress.HostToNetworkOrder((short)0));
      foreach (GenericCertificate pkc in certificates) {
        writer.Write((byte)pkc.Type);

        ReloadGlobals.WriteOpaqueValue(writer, pkc.Certificate, 0xFFFF);
      }
      StreamUtil.WrittenBytesShortExcludeLength(posBeforeCerts, writer);
      signature.Dump(writer);
      return (UInt16)(writer.BaseStream.Position - posBeforeCerts);

    }

    /// <summary>
    /// Deserializes the security block from bytes.
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="reloadMsgSize"></param>
    /// <returns></returns>
    public SecurityBlock FromReader(BinaryReader reader, long reloadMsgSize) {
      var ascii = new ASCIIEncoding();
      var defEncode = Encoding.Default;
      long posBeforeCerts = reader.BaseStream.Position;
      UInt16 certLen = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      while (StreamUtil.ReadBytes(posBeforeCerts, reader) < certLen) {
        var type = (CertificateType)reader.ReadByte();
        UInt16 len = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        //string pkc = defEncode.GetString(reader.ReadBytes(len), 0, len);
        TElX509Certificate cert = new TElX509Certificate();
        Byte[] bcert = reader.ReadBytes(len);
        cert.LoadFromBuffer(bcert);
        certificates.Add(new GenericCertificate(bcert));
      }
      signature = new Signature(m_ReloadConfig).FromReader(reader, reloadMsgSize);
      return this;
    }

    #endregion
  }

  public class ReloadMessage {
    public enum ReadFlags {
      full = 0,
      no_certcheck = 1
    }
    /* This field MUST contain the value 0xd2454c4f (the string
      'RELO' with the high bit of the first byte set.). */
    public static UInt32 RELOTAG = 0xd2454c4f;

    private ReloadConfig m_ReloadConfig;
    public ForwardingHeader forwarding_header;
    public SecurityBlock security_block;
    public RELOAD_MessageBody reload_message_body;

    private NodeId m_LastHopNodeId = null;
    public NodeId LastHopNodeId {
      get { return m_LastHopNodeId; }
      set { m_LastHopNodeId = value; }
    }

    public UInt64 TransactionID {
      get { return forwarding_header.transaction_id; }
    }

    public NodeId OriginatorID {
      //get { return security_block.OriginatorNodeID; }
      get {
        if (forwarding_header.via_list != null && forwarding_header.via_list.Count > 0) {
          if (security_block.OriginatorNodeID != (forwarding_header.via_list.First()).destination_data.node_id)
          {

          }
          return (forwarding_header.via_list.First()).destination_data.node_id;
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "security_block.OriginatorNodeID!!!!!!!!!!!!!!!!!!!!!!!!");
          return security_block.OriginatorNodeID;
        }
      }
    }

    public bool IsRequest() {
      return (((short)reload_message_body.RELOAD_MsgCode & 0x0001) != 0x0000);
    }

    public bool IsFragmented() {
      return (0 != (forwarding_header.fragment & 0x80000000));
    }

    public bool IsSingleFragmentMessage() {
      uint fragment_offset = (forwarding_header.fragment & 0x3FFFFFFF);
      bool last_fragment = ((forwarding_header.fragment & 0x40000000) != 0);

      if (fragment_offset == 0 && last_fragment == true) {
        //single fragment message (means not fragmented)
        return true;
      }
      return false;
    }

    public bool NeedsReassembling() {
      return (IsFragmented() == true && IsSingleFragmentMessage() == false);
    }

    public ReloadMessage(ReloadConfig rc, NodeId OriginatorNodeID,
      Destination destination, UInt64 trans_id, RELOAD_MessageBody reload_content) {
      m_ReloadConfig = rc;
      forwarding_header.version = ReloadGlobals.RELOAD_VERSION;
      forwarding_header.ttl = ReloadGlobals.TTL;
      forwarding_header.overlay = ReloadGlobals.OverlayHash;
      forwarding_header.transaction_id = trans_id;

      reload_message_body = reload_content;

      forwarding_header.via_list = new List<Destination>();
      forwarding_header.via_list.Add(new Destination(OriginatorNodeID));

      forwarding_header.destination_list = new List<Destination>();
      forwarding_header.destination_list.Add(destination);
      SignerIdentity myId = m_ReloadConfig.AccessController.MyIdentity;
      security_block = new SecurityBlock(rc, myId);
      /* Sign the message, create stream of body */
      security_block.SignMessage(ReloadGlobals.OverlayHash,
        trans_id.ToString(), reload_message_body);
    }

    public ReloadMessage(ReloadConfig rc, NodeId LastHopNodeId) {
      m_LastHopNodeId = LastHopNodeId;
      m_ReloadConfig = rc;
    }

    public ReloadMessage(ReloadConfig rc) {
      m_ReloadConfig = rc;
    }

    public bool AddViaHeader(NodeId Node) {
      if (forwarding_header.via_list == null)
        forwarding_header.via_list = new List<Destination>();

      forwarding_header.via_list.Add(new Destination(Node));
      return true;
    }

    public bool PutViaListToDestination(ReloadMessage rmDest) {
      if (forwarding_header.via_list != null) {
        forwarding_header.via_list.Remove(forwarding_header.via_list.First());
        //this function assumes that the final destination already set to destination list
        foreach (Destination dest in forwarding_header.via_list) {
          //using index 0 will actually reverse the via list items, which is required see "3.3. Routing"
          rmDest.forwarding_header.destination_list.Insert(0, dest);
        }
        forwarding_header.via_list.Clear();
      }
      return true;
    }

    public bool PutViaListToDestination() {
      if (forwarding_header.via_list != null) {
        //this function assumes that the final destination already set to destination list
        foreach (Destination dest in forwarding_header.via_list) {
          //using index 0 will actually reverse the via list items, which is required see "3.3. Routing"
          forwarding_header.destination_list.Insert(0, dest);
        }
        //forwarding_header.via_list.Clear();
      }
      return true;
    }

    public void RemoveFirstDestEntry() {
      forwarding_header.destination_list.RemoveAt(0);
    }

    public Destination GetFirstViaHeader() {
      return forwarding_header.via_list.First();
    }

    internal void IncrementTransactionID() {
      forwarding_header.transaction_id = ++m_ReloadConfig.TransactionID;
    }

    /// <summary>
    /// Method is used to retrieve fragmented Messages. Needs to be called for every fragmented Message. Message fragments are stored in the fragmentedMessageBuffer.
    /// </summary>
    /// <param name="fragmentedMessageBuffer">reference to a buffer for MessageFragments</param>
    /// <returns name="fullmessage">Return reassembled Message in case all fragments have been received. Otherwise return value is null.</returns>
    public ReloadMessage ReceiveFragmentedMessage(ref Dictionary<ulong, SortedDictionary<UInt32, MessageFragment>> fragmentedMessageBuffer) {

      try {

        MessageFragment fragment = (MessageFragment)reload_message_body;

        StackTrace stackTrace = new StackTrace();

        if (!fragmentedMessageBuffer.ContainsKey(TransactionID)) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format(stackTrace.GetFrame(1).GetMethod().DeclaringType.FullName + ": FIRST Fragmented Message with TransID: {0} Offset: {1} ", TransactionID, fragment.Offset));
          fragmentedMessageBuffer.Add(TransactionID, new SortedDictionary<UInt32, MessageFragment>());
        }
        else {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format(stackTrace.GetFrame(1).GetMethod().DeclaringType.FullName + ": Fragmented Message with TransID: {0} Offset: {1} ", TransactionID, fragment.Offset));
        }

        fragmentedMessageBuffer[TransactionID][fragment.Offset] = fragment;

        if (fragmentedMessageBuffer[TransactionID].Count >= 2) {  //check if all fragments have been received

          SortedDictionary<UInt32, MessageFragment> dict = fragmentedMessageBuffer[TransactionID];    //sort after fragmentation offset

          if (dict.First().Value.Offset != 0)
            return null;  //first fragment missing

          if (dict.Last().Value.LastFragment != true)
            return null;  //last fragment missing

          int received_payload = 0;
          foreach (KeyValuePair<UInt32, MessageFragment> pair in dict) {
            if (received_payload != pair.Key)
              return null;
            received_payload += pair.Value.PayloadLength;
          }

          //Okay alles da => zusammenpacken

          MemoryStream ms = new MemoryStream();
          BinaryWriter writer = new BinaryWriter(ms);

          bool header = false;
          foreach (KeyValuePair<UInt32, MessageFragment> pair in dict) {
            //we need the header and take the header from the last received message TODO: this can be done better
            if (header == false) {
              //ForwardingHeader forwarding_header = forwarding_header;
              /* First save RELO Tag */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)ReloadMessage.RELOTAG));
              /* overlay */
              //TODO
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)forwarding_header.overlay));
              /* configuration_sequence */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.configuration_sequence));
              /* version */
              writer.Write((Byte)forwarding_header.version);
              /* ttl */
              writer.Write((Byte)forwarding_header.ttl);
              /* fragment */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder(0));    //no longer fragmented
              /* length */
              long forwarding_header_length_pos = ms.Position;
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)forwarding_header.length));
              /* transaction_id */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((long)forwarding_header.transaction_id));
              /* max_response_length */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)forwarding_header.max_response_length));

              /* via_list_length */
              long via_list_length_pos = ms.Position;
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.via_list_length));            //just placeholder
              /* destination_list_length */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.destination_list_length));    //just placeholder
              /* options_length */
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.options_length));             //just placeholder

              forwarding_header.via_list_length = (ushort)ReloadMessage.WriteDestList(writer, forwarding_header.via_list);
              forwarding_header.destination_list_length = (ushort)ReloadMessage.WriteDestList(writer, forwarding_header.destination_list);
              forwarding_header.options_length = (ushort)ReloadMessage.WriteOptionList(writer, forwarding_header.fw_options);

              long headerEndPosition = ms.Position;
              ms.Seek(via_list_length_pos, SeekOrigin.Begin);
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.via_list_length));
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.destination_list_length));
              writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.options_length));
              ms.Seek(headerEndPosition, SeekOrigin.Begin);

              header = true;
            }

            pair.Value.Dump(writer);

          }

          fragmentedMessageBuffer.Remove(TransactionID);
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format("Message reassembled: Length={0}", ms.Length));

          long bytesProcessed = 0;
          byte[] temp = ms.ToArray();
          ReloadMessage fullMsg = new ReloadMessage(m_ReloadConfig).FromBytes(temp, ref bytesProcessed, ReloadMessage.ReadFlags.full);
          fullMsg.LastHopNodeId = this.LastHopNodeId;

          return fullMsg;
        }
        else
          return null;
      }

      catch (Exception ex) {
        throw ex;
      }
    }

    public static int GetDestListNetLength(List<Destination> dest_list) {
      int Length = 0;

      if (dest_list != null) {
        foreach (Destination dest in dest_list) {
          Length += dest.GetNetLength();
        }
      }
      return Length;
    }

    public static long WriteDestList(BinaryWriter writer, List<Destination> dest_list) {
      long length = 0;

      if (dest_list != null) {
        foreach (Destination dest in dest_list) {
          //save uncompressed destination data
          writer.Write((Byte)dest.type);
          length += 1;

          switch (dest.type) {
            case DestinationType.node:
              if (dest.destination_data.node_id.Data.Length == 0)
                throw new System.Exception(String.Format("Destination Node empty!"));
              writer.Write((Byte)dest.destination_data.node_id.Data.Length);
              length += 1;
              writer.Write(dest.destination_data.node_id.Data);
              length += dest.destination_data.node_id.Data.Length;
              break;
            case DestinationType.resource:
              if (dest.destination_data.ressource_id.Data.Length == 0)
                throw new System.Exception(String.Format("Destination Resource empty!"));
              writer.Write((Byte)(dest.destination_data.ressource_id.Data.Length + 1));
              length += 1;
              length += ReloadGlobals.WriteOpaqueValue(writer, dest.destination_data.ressource_id.Data, 0xFF);
              break;
            case DestinationType.compressed:
              /* not implemented */
              break;
          }
        }
      }
      return length;
    }

    public static long WriteOptionList(BinaryWriter writer, List<ForwardingOption> option_list) {
      UInt16 length = 0;

      if (option_list != null) {
        foreach (ForwardingOption option in option_list) {
          //save uncompressed destination data
          writer.Write((Byte)option.fwo_type);
          length += 1;

          writer.Write((Byte)option.flags);
          length += 1;
          /* option_length_pos */
          long option_length_pos = writer.BaseStream.Position;
          writer.Write((UInt16)System.Net.IPAddress.HostToNetworkOrder(length));   //just placeholder
          length += 2;

          writer.Write(option.bytes);
          length += (UInt16)option.bytes.Length;

          long end_pos = writer.BaseStream.Position;
          writer.BaseStream.Seek(option_length_pos, SeekOrigin.Begin);                    //seek the length field
          writer.Write((UInt16)System.Net.IPAddress.HostToNetworkOrder((short)option.bytes.Length));   //fill in the final option length
          writer.BaseStream.Seek(end_pos, SeekOrigin.Begin);                              //restore writer position at the end

          switch (option.fwo_type) {
            case ForwardingOptionsType.destinationOverlay:
              /* not implemented */
              break;
          }
        }
      }
      return length;
    }

    public List<Destination> ReadDestList(BinaryReader reader, int Length) {
      List<Destination> destination_list = new List<Destination>();

      if (Length != 0) {
        int counted_bytes = Length;

        while (counted_bytes > 2) {
          DestinationType type = (DestinationType)reader.ReadByte();
          --counted_bytes;
          Byte destination_length = reader.ReadByte();
          --counted_bytes;

          switch (type) {
            case DestinationType.node:
              destination_list.Add(new Destination(new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS))));
              counted_bytes -= ReloadGlobals.NODE_ID_DIGITS;
              break;
            case DestinationType.resource:
              Byte length = reader.ReadByte();
              --counted_bytes;
              if (length == 0)
                throw new System.Exception("Resource ID length == 0!");
              destination_list.Add(new Destination(new ResourceId(reader.ReadBytes(length))));
              counted_bytes -= length;
              break;
            case DestinationType.compressed:
              /* not implemented */
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "DestinationType 'compressed' is not implemented!");
              //throw new System.Exception("DestinationType 'compressed' is not implemented!");
              break;
          }
          if (counted_bytes == 1) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "invalid length of destination element");
          }
        }

        if (counted_bytes != 0) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "invalid length of destination element");
          // throw new System.Exception(String.Format("invalid length of destination  element"));
        }
      }
      return destination_list;
    }

    public List<ForwardingOption> ReadOptionList(BinaryReader reader, int Length) {
      List<ForwardingOption> option_list = new List<ForwardingOption>();

      if (Length != 0) {
        int counted_bytes = Length;

        while (counted_bytes > 3) {
          ForwardingOption forw_opt = new ForwardingOption();

          forw_opt.fwo_type = (ForwardingOptionsType)reader.ReadByte();
          counted_bytes--;
          forw_opt.flags = reader.ReadByte();
          counted_bytes--;

          forw_opt.length = (UInt16)System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt16());
          counted_bytes = counted_bytes - 2;

          forw_opt.bytes = reader.ReadBytes(forw_opt.length);
          counted_bytes = counted_bytes - forw_opt.length;
          option_list.Add(forw_opt);
        }
        if (counted_bytes != 0) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "invalid length within ForwardingOptions");
        }
      }
      return option_list;
    }

    /// <summary>
    /// Method is used to fragmented a Message into fragments.
    /// </summary>
    /// <param name="fragmentSize">Size at which the Message is spitted into fragments</param>
    /// <returns name="fragmentlist">Each entry in this List is RELOAD compliant MessageFragment</returns>
    public virtual List<byte[]> ToBytesFragmented(uint fragmentSize)    // TODO: this is probably not yet perfect
    {
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, "ToBytesFragmented: Start");
      List<byte[]> resultList = new List<byte[]>();

      byte[] message = this.ToBytes();
      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, "ToBytesFragmented: Start Total Message Length = " + message.Length);
      MemoryStream messageStream = new MemoryStream(message);
      BinaryReader reader = new BinaryReader(messageStream);
      uint message_length = (uint)messageStream.Length;
      uint forwarding_header_length =
          4 + //RELOTAG
          4 + //overlay
          2 + //configuration_sequence
          1 + //version
          1 + //ttl
          4 + //fragment
          4 + //length
          8 + //transaction_id
          4 + //max_response_length
          2 + //via_list_length
          2 + //destination_list_length
          2 + //options_length
          (uint)forwarding_header.via_list_length +
          (uint)forwarding_header.destination_list_length +
          (uint)forwarding_header.options_length;

      //how many messages do we need?
      uint payload_bytes = message_length - forwarding_header_length;

      uint count = (uint)Math.Ceiling((double)payload_bytes / (double)(fragmentSize - forwarding_header_length));

      for (uint i = 0; i < count; i++) {
        uint offset = 0;
        MemoryStream ms = new MemoryStream();

        //long test = 0;
        //ReloadMessage hmmm = new ReloadMessage(m_ReloadConfig).FromBytes(message, ref test, ReadFlags.full);

        using (BinaryWriter writer = new BinaryWriter(ms)) {
          //header first

          writer.Write(message, 0, (int)forwarding_header_length);
          if (i == 0)
            messageStream.Seek(forwarding_header_length, SeekOrigin.Begin);

          long afterHeaderPosition = ms.Position; //should be equal to forwarding_header_length?

          int bytesToRead = (int)Math.Min(fragmentSize - forwarding_header_length, messageStream.Length - messageStream.Position);

          uint fragment = offset | (0x80000000);  //fragment Bit
          fragment = (uint)(messageStream.Position - forwarding_header_length);
          fragment = fragment | (0x80000000);
          writer.Write(reader.ReadBytes(bytesToRead));
          offset += (uint)bytesToRead;
          long frameEndPosition = ms.Position;

          ms.Seek(12, SeekOrigin.Begin);    //fragment position in header

          if (i == count - 1)  //last fragment
                    {
            fragment = (fragment | (0x40000000));
          }
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)fragment));
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)frameEndPosition)); //Test

          if (i == 0) //fill in message size in first fragment!
                    {
            //go to position of message length tag after the 2 bytes message code tag
            //ms.Seek(forwarding_header_length + 2, SeekOrigin.Begin);
            //writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)message_length));
          }
          ms.Seek(frameEndPosition, SeekOrigin.Begin);
          resultList.Add(ms.ToArray());
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, "ToBytesFragmented: " + TransactionID + " Fragment No. " + (i + 1) + "/" + count + "Offset=" + offset + "length=" + frameEndPosition);
        }


      }
      return resultList;


    }

    public virtual byte[] ToBytes() {
      MemoryStream ms = new MemoryStream();
      using (BinaryWriter writer = new BinaryWriter(ms)) {
        /* First save RELO Tag */
        writer.Write(IPAddress.HostToNetworkOrder((int)RELOTAG));
        /* overlay */
        //TODO
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)forwarding_header.overlay));
        /* configuration_sequence */
        writer.Write(IPAddress.HostToNetworkOrder(
          (short)forwarding_header.configuration_sequence));
        /* version */
        writer.Write((Byte)forwarding_header.version);
        /* ttl */
        writer.Write((Byte)forwarding_header.ttl);
        /* fragment */
        /* If the message is not
           fragmented, it is simply treated as if it is the only fragment:  the
           last fragment bit is set and the offset is 0 resulting in a fragment
           value of 0xC0000000. */
        /* fragmented messages are processed by ToBytesFragmented*/
        forwarding_header.fragment = 0xC0000000;
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)forwarding_header.fragment));
        /* length */
        long forwarding_header_length_pos = ms.Position;
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)forwarding_header.length));
        /* transaction_id */
        writer.Write(IPAddress.HostToNetworkOrder(
          (long)forwarding_header.transaction_id));
        /* max_response_length */
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)forwarding_header.max_response_length));
        /* via_list_length */
        long via_list_length_pos = ms.Position;
        writer.Write(IPAddress.HostToNetworkOrder(
          (short)forwarding_header.via_list_length));
        /* destination_list_length */
        writer.Write(IPAddress.HostToNetworkOrder(
          (short)forwarding_header.destination_list_length));
        /* options */
        writer.Write(IPAddress.HostToNetworkOrder(
          (short)forwarding_header.options_length));

        forwarding_header.via_list_length = (ushort)ReloadMessage.WriteDestList(
          writer, forwarding_header.via_list);
        forwarding_header.destination_list_length = (ushort)ReloadMessage.WriteDestList(
          writer, forwarding_header.destination_list);
        forwarding_header.options_length = (ushort)ReloadMessage.WriteOptionList(
        writer, forwarding_header.fw_options);

        long BeforeMsgPosition = ms.Position;
        long msEnd = ms.Position;
        try {
          reload_message_body.Dump(writer);
          msEnd = ms.Position;
          //go to position of message length tag after the 2 bytes message code tag
          ms.Seek(BeforeMsgPosition + 2, SeekOrigin.Begin);
          //write the four bytes message length value minus code tag length (2 bytes) and the length value (4 bytes)
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)(msEnd - BeforeMsgPosition - 6)));

          ms.Seek(via_list_length_pos, SeekOrigin.Begin);
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.via_list_length));
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.destination_list_length));
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)forwarding_header.options_length));

          ms.Seek(msEnd, SeekOrigin.Begin);
        }
        catch (Exception e) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("Msg Body Dump(): {0}", e.Message));
        }

        if (this.IsFragmented() && this.IsSingleFragmentMessage() == false) {
          //if msg is (real) fragmented there are no more bytes after the fragment body

          //length of this fragment
          forwarding_header.length = (uint)msEnd;

          //fill in length
          ms.Seek(forwarding_header_length_pos, SeekOrigin.Begin);
          writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)forwarding_header.length));

          //seek end and return
          ms.Seek(msEnd, SeekOrigin.Begin);
          return ms.ToArray();
        }

        UInt32 SizeOfMessageExtensions = 0;
        // no message extensions so far -> length null 
        writer.Write(IPAddress.HostToNetworkOrder((int)SizeOfMessageExtensions));

        //long Endposition = ms.Position;

        //go to position of message length tag after the 2 bytes message code tag
        //ms.Seek(BeforeMsgPosition + 2, SeekOrigin.Begin);
        /* write the four bytes message length value minus length and code tag
         * length and message extension size which is 6+4
         */
        //long msgBodyLength = Endposition - BeforeMsgPosition - 10;
        //writer.Write(IPAddress.HostToNetworkOrder((int)(msgBodyLength)));

        //ms.Seek(Endposition, SeekOrigin.Begin);

        if (ReloadGlobals.TLS &&
          security_block.Certificates.Count != 0) {
          security_block.Dump(writer);
        }
        else {
          //FAKE FAKE! this is only used for simple transport
          ReloadGlobals.WriteOpaqueValue(writer, security_block.OriginatorNodeID.Data, 0xFF);
          ReloadGlobals.WriteOpaqueValue(writer, m_ReloadConfig.LocalNodeID.Data, 0xFF);
        }

        long GlobalEndposition = ms.Position;
        //ms.Seek(via_list_length_pos, SeekOrigin.Begin);
        //writer.Write(IPAddress.HostToNetworkOrder((short)forwarding_header.via_list_length));
        //writer.Write(IPAddress.HostToNetworkOrder((short)forwarding_header.destination_list_length));

        forwarding_header.length = (uint)GlobalEndposition;//TEST Endposition;
        ms.Seek(forwarding_header_length_pos, SeekOrigin.Begin);
        writer.Write(IPAddress.HostToNetworkOrder((int)forwarding_header.length));

        ms.Seek(GlobalEndposition, SeekOrigin.Begin);
      }
      return ms.ToArray();
    } // End ToBytes()

    public ReloadMessage FromBytes(byte[] bytes, ref long offset, ReadFlags flags) {
      if (bytes == null) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          "FromBytes: bytes = null!!");
        return null;
      }

      if (offset != 0)
        offset = 0;

      var ms = new MemoryStream(bytes, (int)offset, (int)(bytes.Count() - offset));
      //prepare variable for return (ref value)
      offset = 0;

      using (var reader = new BinaryReader(ms)) {
        try {
          UInt32 RELO_Tag;
          //first check if this is a RELOAD message by checking th RELO tag
          RELO_Tag = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());

          if (RELO_Tag != RELOTAG) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("==> message has no RELO tag"));
            return null;
          }
          forwarding_header.overlay = (UInt32)IPAddress.NetworkToHostOrder(
            reader.ReadInt32());

          if (forwarding_header.overlay != ReloadGlobals.OverlayHash)
              throw new System.Exception("Message from wrong overlay! (probably invalid hash)");

          /* configuration_sequence */
          forwarding_header.configuration_sequence = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          /* version */
          forwarding_header.version = reader.ReadByte();
          /* ttl */
          forwarding_header.ttl = reader.ReadByte();
          /* fragment */
          forwarding_header.fragment = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());
          /* length */
          forwarding_header.length = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());

          if (forwarding_header.length >= ReloadGlobals.MAX_PACKET_BUFFER_SIZE) {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("==> forwarding header exceeds maximum value of {0}",
              ReloadGlobals.MAX_PACKET_BUFFER_SIZE));
            return null;
          }

          /* transaction_id */
          forwarding_header.transaction_id = (UInt64)IPAddress.NetworkToHostOrder(reader.ReadInt64());
          /* max_response_length */
          forwarding_header.max_response_length = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());
          /* via list length */
          forwarding_header.via_list_length = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          /* destination list length */
          forwarding_header.destination_list_length = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          /* options length */
          forwarding_header.options_length = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());

          if (forwarding_header.via_list_length != 0)
            forwarding_header.via_list = ReadDestList(reader, forwarding_header.via_list_length);

          if (forwarding_header.destination_list_length != 0)
            forwarding_header.destination_list = ReadDestList(reader, forwarding_header.destination_list_length);

          if (forwarding_header.options_length != 0)
            forwarding_header.fw_options = ReadOptionList(reader, forwarding_header.options_length);

          long reload_msg_begin = ms.Position;

          if (0 != (forwarding_header.fragment & 0x80000000)) { //is this a fragment?
            uint fragment_offset = (forwarding_header.fragment & 0x3FFFFFFF);
            bool last_fragment = ((forwarding_header.fragment & 0x40000000) != 0);

            if (fragment_offset == 0 && last_fragment == true) {
              //single fragment message (means not fragmented) => process as usual
            }
            else {
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FRAGMENTATION, String.Format("Fragmented Message: offset " + fragment_offset + "READ bytes " + (forwarding_header.length - reload_msg_begin)) + " left " + (reader.BaseStream.Length - ms.Position));
              reload_message_body = new MessageFragment(RELOAD_MessageCode.Invalid, fragment_offset, last_fragment).FromReader(this, reader, forwarding_header.length - reload_msg_begin);  //this should be the fragmentsize instead of ms.Length better use forwardingheader.length TODO: FIX it
              offset = ms.Position;
              return this;
            }
          }

          // now turn to message body
          RELOAD_MessageCode MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          // now read message length          
          long reload_msg_size = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());

          /* set pointer before msg code that the message routines itself
           * can read it again
           */
          ms.Seek(reload_msg_begin, SeekOrigin.Begin);
          switch (MsgCode) {
            case RELOAD_MessageCode.Probe_Request:
            case RELOAD_MessageCode.Probe_Answer:
              break;
            case RELOAD_MessageCode.Attach_Request:
            case RELOAD_MessageCode.Attach_Answer:
              reload_message_body = new AttachReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Store_Request:
              reload_message_body = new StoreReq(
                m_ReloadConfig.ThisMachine.UsageManager).FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Store_Answer:
              reload_message_body = new StoreAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Fetch_Request:
              reload_message_body = new FetchReq(m_ReloadConfig.ThisMachine.UsageManager).FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Fetch_Answer:
              reload_message_body = new FetchAns(m_ReloadConfig.ThisMachine.UsageManager).FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Remove_Request:
            case RELOAD_MessageCode.Remove_Answer:
              //reload_message_body = new RemoveReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Find_Request:
            case RELOAD_MessageCode.Find_Answer:
              break;
            case RELOAD_MessageCode.Join_Request:
            case RELOAD_MessageCode.Join_Answer:
              reload_message_body = new JoinReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Leave_Request:
            case RELOAD_MessageCode.Leave_Answer:
              reload_message_body = new LeaveReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Update_Request:
            case RELOAD_MessageCode.Update_Answer:
              reload_message_body = new UpdateReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Route_Query_Request:
            case RELOAD_MessageCode.Route_Query_Answer:
              break;
            case RELOAD_MessageCode.Ping_Request:
            case RELOAD_MessageCode.Ping_Answer:
              reload_message_body = new PingReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Stat_Request:
            case RELOAD_MessageCode.Stat_Answer:
              break;
            case RELOAD_MessageCode.App_Attach_Request:
            case RELOAD_MessageCode.App_Attach_Answer:
              reload_message_body = new AppAttachReqAns().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Error:
              reload_message_body = new ErrorResponse().FromReader(this, reader, reload_msg_size);
              break;
            case RELOAD_MessageCode.Unused:
            case RELOAD_MessageCode.Unused2:
            default:
              throw new System.Exception(String.Format("Invalid RELOAD message type {0}", MsgCode));
          }
          if (reload_message_body != null)
            reload_message_body.RELOAD_MsgCode = MsgCode;

          // now read message extension length
          UInt32 message_extension_size = (UInt32)IPAddress.NetworkToHostOrder(
            reader.ReadInt32());
          if (message_extension_size != 0) {
            //skip message extension length
            reader.ReadBytes((int)message_extension_size);
          }
          if (ReloadGlobals.TLS) {
            /* Obtain security block */
            security_block = new SecurityBlock(m_ReloadConfig).FromReader(reader, reload_msg_size);
          }
          else { // do the FAKE
            int iNodeIDLen = reader.ReadByte();

            if (iNodeIDLen != 0) {
              NodeId nodeid = new NodeId(reader.ReadBytes(iNodeIDLen));
              if (flags != ReadFlags.no_certcheck) {
                if (ReloadGlobals.TLS && nodeid != security_block.OriginatorNodeID)
                  throw new System.Exception("Wrong message Originator!");
                security_block.OriginatorNodeID = nodeid;
              }
            }
            else
              throw new System.Exception("Originator of message cannot be read!");
            int iLHNodeIDLen = reader.ReadByte();
            if (iLHNodeIDLen != 0)
              LastHopNodeId = new NodeId(reader.ReadBytes(iLHNodeIDLen));
          }
          offset = ms.Position;
          return this;
        } // End mok security
        catch (Exception ex) {
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            "ReloadMesssage.FromBytes(): " + ex.Message);
          return null;
        }
      }
    }


    #region Proprietary
    //Proprietary  //--Joscha		  
    //Reverse and set the ForwardingOptionsType.sourceOverlay and ForwardingOptionsType.destinationOverlay to recmsg
    public void addOverlayForwardingOptions(ReloadMessage recmsg) {
      if (recmsg.forwarding_header.via_list != null) {
        foreach (Destination destx in recmsg.forwarding_header.via_list)
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
            String.Format("addOverlayForwardingOptions Via={0} ", destx.ToString()));
      }
      if (recmsg.forwarding_header.destination_list != null) {
        foreach (Destination desty in recmsg.forwarding_header.destination_list)
          m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
            String.Format("addOverlayForwardingOptions Dest={0} ", desty.ToString()));
      }

      if (recmsg.forwarding_header.fw_options != null) {
        this.forwarding_header.fw_options = new List<ForwardingOption>();
        foreach (ForwardingOption option in recmsg.forwarding_header.fw_options) {
          if (option.fwo_type == ForwardingOptionsType.sourceOverlay) {
            string source_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);

            ForwardingOption answerOption = new ForwardingOption();
            answerOption.fwo_type = ForwardingOptionsType.destinationOverlay;


            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(source_overlay); // TODO: Unicode for sure?
            answerOption.bytes = bytes;
            answerOption.length = (UInt16)bytes.Length;

            this.forwarding_header.fw_options.Add(answerOption);

            //if the message is stored by the GateWay itself the storeanswer has to be processed by the Gateway again
            //if (m_ReloadConfig.ThisMachine is GWMachine)
            //  recmsg.LastHopNodeId = m_ReloadConfig.ThisMachine.Topology.Id;
          }
          if (option.fwo_type == ForwardingOptionsType.destinationOverlay) {
            string destination_overlay = System.Text.Encoding.Unicode.GetString(option.bytes);

            ForwardingOption answerOption = new ForwardingOption();
            answerOption.fwo_type = ForwardingOptionsType.sourceOverlay;


            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(destination_overlay); // TODO: Unicode for sure?
            answerOption.bytes = bytes;
            answerOption.length = (UInt16)bytes.Length;

            this.forwarding_header.fw_options.Add(answerOption);
            //if the message is stored by the GateWay itself the storeanswer has to be processed by the Gateway again
            //if (m_ReloadConfig.ThisMachine is GWMachine)
            //  recmsg.LastHopNodeId = m_ReloadConfig.ThisMachine.Topology.Id;
          }
        }
      }
    }

    // if resourceName contains an @ and the string behind the @ is different from the current OverlayName the ForwardingOptionsType.destinationOverlay is set
    public bool AddDestinationOverlay(string resourceName) {
      String destinationOverlay = null;
      if (ReloadGlobals.OverlayName == null)
        return false;
      else if (!resourceName.Contains(ReloadGlobals.OverlayName) && resourceName.Contains("@"))
      {
        destinationOverlay = resourceName.Substring(resourceName.IndexOf("@") + 1);
      }
      else if (resourceName != null) {

        destinationOverlay = resourceName;   //TODO: remove?
      }
      else
        return false;

      if (forwarding_header.fw_options == null) {
        forwarding_header.fw_options = new List<ForwardingOption>();
      }

      ForwardingOption option;
      byte[] bytes;

      //destinationOverlay
      option = new ForwardingOption();
      option.fwo_type = ForwardingOptionsType.destinationOverlay;


      bytes = System.Text.Encoding.Unicode.GetBytes(destinationOverlay); // TODO: Unicode for sure?
      option.bytes = bytes;
      option.length = (UInt16)bytes.Length;
      forwarding_header.fw_options.Add(option);

      option = new ForwardingOption();
      option.fwo_type = ForwardingOptionsType.sourceOverlay;
      bytes = System.Text.Encoding.Unicode.GetBytes(ReloadGlobals.OverlayName); // TODO: Unicode for sure?
      option.bytes = bytes;
      option.length = (UInt16)bytes.Length;
      //forwarding_header.fw_options.Add(option);

      return true;    //TODO:


    }
  }

    #endregion

  public class RELOAD_MessageBody {
    public RELOAD_MessageCode RELOAD_MsgCode;
    public virtual UInt32 Dump(BinaryWriter writer) { return 0; }
    public virtual RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) { return null; }
  }

  public class MessageFragment : RELOAD_MessageBody {
    private Byte[] fragment = null;
    private uint m_offset;
    public uint Offset {
      get { return m_offset; }
      set { m_offset = value; }
    }

    private bool m_lastfragment;
    public bool LastFragment {
      get { return m_lastfragment; }
      set { m_lastfragment = value; }
    }

    public MessageFragment(RELOAD_MessageCode MsgCode, uint offset, bool last_fragment) {
      RELOAD_MsgCode = MsgCode;
      m_offset = offset;
      m_lastfragment = last_fragment;
    }

    public MessageFragment() {
      RELOAD_MsgCode = RELOAD_MessageCode.Error;  //TODO: no MsgCode defined?
    }

    public int PayloadLength {
      get {
        if (fragment != null)
          return fragment.Length;
        else
          return 0;
      }
    }


    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      try {
        writer.Write(fragment);
        length += (UInt32)fragment.Length;
      }
      catch (Exception ex) {
        throw ex;
      }
      length = (UInt32)fragment.Length;
      return length;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {
      try {
        if ((reader.BaseStream.Length - reader.BaseStream.Position) < reload_msg_size) {
          return null;
        }
        fragment = reader.ReadBytes((int)reload_msg_size);
      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  public class ErrorResponse : RELOAD_MessageBody {
    private String m_errmsg;
    public String ErrorMsg {
      get { return m_errmsg; }
      set { m_errmsg = value; }
    }

    private RELOAD_ErrorCode m_errcode;
    public RELOAD_ErrorCode ErrorCode {
      get { return m_errcode; }
      set { m_errcode = value; }
    }

    public ErrorResponse(RELOAD_ErrorCode errcode, String errmsg) {
      RELOAD_MsgCode = RELOAD_MessageCode.Error;
      m_errcode = errcode;
      m_errmsg = errmsg;
    }

    public ErrorResponse() {
      RELOAD_MsgCode = RELOAD_MessageCode.Error;
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      writer.Write(IPAddress.HostToNetworkOrder((short)ErrorCode));
      length = length + 2;
      /* The The ICE password, we set it to zero length in NO-ICE */
      length = length + ReloadGlobals.WriteOpaqueValue(writer, Encoding.UTF8.GetBytes(m_errmsg), 0xFFFF);

      return length;
    }
    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a ErrorResponse packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        short length;

        m_errcode = (RELOAD_ErrorCode)(UInt16)(IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
        reload_msg_size = reload_msg_size - 2;

        /* get the response message */
        length = IPAddress.HostToNetworkOrder((short)reader.ReadInt16());
        m_errmsg = Encoding.UTF8.GetString(reader.ReadBytes(length), 0, length);

        reload_msg_size = reload_msg_size - (length + 1);
      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  public class JoinReqAns : RELOAD_MessageBody {
    private NodeId m_ID;
    public NodeId JoiningNode {
      get { return m_ID; }
      set { m_ID = value; }
    }

    public JoinReqAns(Node joining_node, bool req) {
      RELOAD_MsgCode = req ? RELOAD_MessageCode.Join_Request : RELOAD_MessageCode.Join_Answer;

      if (req) {
        m_ID = joining_node.Id;
      }
    }

    public JoinReqAns() {
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      if (RELOAD_MsgCode == RELOAD_MessageCode.Join_Request) {
        length += (UInt32)ReloadGlobals.NODE_ID_DIGITS;
        writer.Write(m_ID.Data);
      }
      //writer.Write((byte)0);
      ReloadGlobals.WriteOpaqueValue(writer, new System.Text.ASCIIEncoding().GetBytes("NONE"), 0xFFFF);
      return length + 1;
    }
    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a JoinReqAns packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        int length = 0;

        if (RELOAD_MsgCode == RELOAD_MessageCode.Join_Request) {
          m_ID = new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS));
          length += ReloadGlobals.NODE_ID_DIGITS;
        }
        UInt16 overlay_specific_dataLen = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        byte[] overlay_specific_data = reader.ReadBytes(overlay_specific_dataLen);

        //skip the overlay_specific_data field (still not defined for Join)
        reload_msg_size = reload_msg_size - (length + 1);
      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  public class LeaveReqAns : RELOAD_MessageBody {
    private NodeId m_ID;
    public NodeId LeavingNode {
      get { return m_ID; }
      set { m_ID = value; }
    }

    public LeaveReqAns(Node leaving_node, bool req) {
      RELOAD_MsgCode = req ? RELOAD_MessageCode.Leave_Request : RELOAD_MessageCode.Leave_Answer;

      if (req) {
        m_ID = leaving_node.Id;
      }
    }

    public LeaveReqAns() {
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      if (RELOAD_MsgCode == RELOAD_MessageCode.Leave_Request) {
        length += (UInt32)ReloadGlobals.NODE_ID_DIGITS;
        writer.Write(m_ID.Data);
      }
      writer.Write((byte)0);
      return length + 1;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a LeaveReqAns packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        int length = 0;

        if (RELOAD_MsgCode == RELOAD_MessageCode.Leave_Request) {
          m_ID = new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS));
          length += ReloadGlobals.NODE_ID_DIGITS;
        }
        reader.ReadByte();

        //skip the overlay_specific_data field (still not defined for Join)
        reload_msg_size = reload_msg_size - (length + 1);


      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  public class PingReqAns : RELOAD_MessageBody {
    private UInt64 m_response_id;
    private bool received_ping=false;
    public UInt64 ResponseID {
      get { return m_response_id; }
      set { m_response_id = value; }
    }

    private UInt64 m_response_time;
    public UInt64 ResponseTime {
      get { return m_response_time; }
    }

    public PingReqAns(UInt64 response_id, bool req) {
      if (!req) {
        m_response_id = response_id;
      }
      RELOAD_MsgCode = req ? RELOAD_MessageCode.Ping_Request : RELOAD_MsgCode = RELOAD_MessageCode.Ping_Answer;
    }

    public PingReqAns() {
      received_ping = true;
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      if (RELOAD_MsgCode == RELOAD_MessageCode.Ping_Answer) {
        writer.Write(IPAddress.HostToNetworkOrder((long)m_response_id));
        length = length + 4;
        if (received_ping == true)
          writer.Write(IPAddress.HostToNetworkOrder((long)m_response_time));  //for signature verification
        else
          writer.Write(IPAddress.HostToNetworkOrder((long)DateTime.Now.Ticks));
        length = length + 4;
      }
      return length;
    }
    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a PingReqAns packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.NetworkToHostOrder(reader.ReadInt32()));

        if (RELOAD_MsgCode == RELOAD_MessageCode.Ping_Answer) {
          m_response_id = (UInt64)IPAddress.NetworkToHostOrder(reader.ReadInt64());
          m_response_time = (UInt64)IPAddress.NetworkToHostOrder(reader.ReadInt64());
          reload_msg_size = reload_msg_size - 8;
        }
        else {
          reader.ReadBytes((int)message_len);
          reload_msg_size = reload_msg_size - message_len;
        }
      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  /// <summary>
  /// A StoreReq message is a sequence of StoreKindData values, each of
  /// which represents a sequence of stored values for a given kind.
  /// RELOAD base -12 p. 86
  /// --alex
  /// </summary>
  public class StoreReq : RELOAD_MessageBody {
    private ResourceId resourceId;
    private byte replica_number;
    private List<StoreKindData> store_kind_data;

    private UsageManager myManager;

    public StoreReq(ResourceId resId, List<StoreKindData> store_kind_data,
      UsageManager manager) {
      resourceId = resId;
      this.RELOAD_MsgCode = RELOAD_MessageCode.Store_Request;
      if (store_kind_data != null && store_kind_data.Count != 0) {
        this.store_kind_data = store_kind_data;
      }
      myManager = manager;
    }

    public StoreReq(UsageManager manager) {
      store_kind_data = new List<StoreKindData>();
      myManager = manager;
    }

    public ResourceId ResourceId {
      get { return this.resourceId; }
    }

    public byte Replica_number {
      get { return this.replica_number; }
    }

    public List<StoreKindData> StoreKindData {
      get { return this.store_kind_data; }
    }

    /// <summary>
    /// Increments the replica_number for this request.
    /// </summary>
    /// <returns>The replica_number base draft -12 p.86</returns>
    public byte incrementReplicaNumber() {
      return ++this.replica_number;
    }

    /// <summary>
    /// Adds a new StoreKindData to the request
    /// </summary>
    /// <param name="storeKind">The Kind to be stored</param>
    /// <returns>The total number of differnt Kinds to be stored</returns>
    public int appendStoreKindData(StoreKindData storeKind) {
      if (storeKind.Values.Count != 0) {
        store_kind_data.Add(storeKind);
        return store_kind_data.Count;
      }
      throw new SystemException("StoreKindData parameter is null");
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder(
        (short)RELOAD_MessageCode.Store_Request));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));
      /* Write Resource-Id */
      ReloadGlobals.WriteOpaqueValue(writer, resourceId.Data, 0XFF);
      /* Write replica number */
      writer.Write((Byte)replica_number);
      /* Placeholder for length of 
       * StoreKindDatas (StoreKindData kind_data<0..2^32-1>;) */
      long posBeforeSDKs = writer.BaseStream.Position;
      writer.Write(IPAddress.HostToNetworkOrder((int)0));

      foreach (StoreKindData kind in store_kind_data) {
        /* Write the kind id */
        writer.Write(IPAddress.HostToNetworkOrder((int)kind.Kind));
        /* Write the gen counter */
        writer.Write(IPAddress.HostToNetworkOrder((long)kind.Generation_counter));
        /* Placeholder for length of all StoredData (StoredData values<0..2^32-1>;) */
        long posBeforeSDs = writer.BaseStream.Position;
        writer.Write(IPAddress.HostToNetworkOrder((int)0));
        foreach (StoredData stored_data in kind.Values) {
          /* Placeholder */
          long posBeforeSD = writer.BaseStream.Position;
          writer.Write(IPAddress.HostToNetworkOrder(0));
          writer.Write(IPAddress.HostToNetworkOrder((long)stored_data.StoreageTime));
          writer.Write(IPAddress.HostToNetworkOrder((int)stored_data.LifeTime));
          stored_data.Value.Dump(writer);
          stored_data.Value.GetUsageValue.dump(writer);
          stored_data.SignData(ResourceId, kind.Kind,
            myManager.m_ReloadConfig.AccessController.MyIdentity,
            myManager.m_ReloadConfig);
          stored_data.Signature.Dump(writer);
          StreamUtil.WrittenBytesExcludeLength(posBeforeSD, writer);
          //length += stored_data.Length;
        }
        /* Write the amount of bytes written for StoredData objects */
        StreamUtil.WrittenBytesExcludeLength(posBeforeSDs, writer);
      }
      StreamUtil.WrittenBytesExcludeLength(posBeforeSDKs, writer);

      return length; // length is obsolet
    }

    /// <summary>
    /// Deserializes a StoreReq message from wire.
    /// </summary>
    /// <param name="rm"></param>
    /// <param name="reader"></param>
    /// <param name="reload_msg_size"></param>
    /// <returns></returns>
    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {
      
      UInt32 message_len = 0;
      /* try to read the packet as a StoreReq packet */
      try
      {
        long posBeforeMsg = reader.BaseStream.Position;
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        Byte res_id_length = reader.ReadByte();
        if (res_id_length == 0)
          throw new System.Exception("Resource ID length == 0!");
        resourceId = new ResourceId(reader.ReadBytes(res_id_length));
        replica_number = reader.ReadByte();

        long posBeforeRead = reader.BaseStream.Position;
        UInt32 kindDataLen = (UInt32)(IPAddress.NetworkToHostOrder(reader.ReadInt32()));
        /* StoreKindData Receive loop */
        while (StreamUtil.ReadBytes(posBeforeRead, reader) < kindDataLen)
        {

          UInt32 kindId = (UInt32)(IPAddress.HostToNetworkOrder(reader.ReadInt32()));
          UInt64 generation = (UInt64)(IPAddress.NetworkToHostOrder(reader.ReadInt64()));

          var store_kind_data = new StoreKindData(kindId, generation);

          long posBeforeSD = reader.BaseStream.Position;
          UInt32 storedDataLen = (UInt32)(IPAddress.HostToNetworkOrder(reader.ReadInt32()));

          if (RELOAD_MsgCode == RELOAD_MessageCode.Store_Request)
          {
            while (StreamUtil.ReadBytes(posBeforeSD, reader) < storedDataLen)
            {
              /* reading properties of StoredData struct */
              UInt32 stored_data_lenght = (UInt32)(IPAddress.NetworkToHostOrder(reader.ReadInt32()));
              UInt64 storage_time = (UInt64)(IPAddress.NetworkToHostOrder(reader.ReadInt64()));
              UInt32 lifetime = (UInt32)(IPAddress.NetworkToHostOrder(reader.ReadInt32()));

              ReloadGlobals.DataModel data_model = myManager.GetDataModelfromKindId(store_kind_data.Kind);

              Boolean exists;
              IUsage usage;
              StoredDataValue stored_data_value;

              switch (data_model)
              {
                case ReloadGlobals.DataModel.SINGLE_VALUE:
                  throw new NotImplementedException("There is no Usage with Single Value atm");

                case ReloadGlobals.DataModel.ARRAY:
                  UInt32 index = (UInt32)(IPAddress.NetworkToHostOrder((int)reader.ReadInt32()));
                  exists = (reader.ReadByte() == 0x00 ? false : true);
                  usage = myManager.GetUsageFromReader(rm, reader, reload_msg_size, store_kind_data.Kind);

                  stored_data_value = new StoredDataValue(index, usage, exists);
                  break;

                case ReloadGlobals.DataModel.DICTIONARY:
                  UInt16 keyLength = (UInt16)(IPAddress.NetworkToHostOrder((short)reader.ReadInt16()));
                  string key = BitConverter.ToString(reader.ReadBytes(keyLength), 0, keyLength);  //key is a hex string
                  key = key.Replace("-", "");
                  exists = (reader.ReadByte() == 0x00 ? false : true);
                  usage = myManager.GetUsageFromReader(rm, reader, reload_msg_size, store_kind_data.Kind);

                  stored_data_value = new StoredDataValue(key, usage, exists);
                  break;

                default:
                  throw new NotSupportedException(String.Format("The data_model {0} is not supported", data_model));
              }
              StoredData stored_data = new StoredData(storage_time, lifetime, stored_data_value);
              stored_data.Signature = new Signature(myManager.m_ReloadConfig).FromReader(reader, reload_msg_size);
              // TODO Process signature
              store_kind_data.Add(stored_data);
              appendStoreKindData(store_kind_data);
            }
          }
        }
        UInt32 totalRead = StreamUtil.ReadBytes(posBeforeMsg, reader);
        reload_msg_size = reload_msg_size - (totalRead + 1);
      }
      catch (Exception ex)
      {
        throw ex;
      }
      return this;
    }
  }

  /// <summary>
  /// In response to a successful Store request the peer MUST return a
  /// StoreAns message containing a series of StoreKindResponse elements
  /// containing the current value of the generation counter for each
  /// Kind-ID, as well as a list of the peers where the data will be
  /// replicated by the node processing the request.
  /// 
  /// see RELOAD base -12 p. 90
  /// --alex
  /// </summary>
  public class StoreAns : RELOAD_MessageBody {

    List<StoreKindResponse> kind_responses;

    public StoreAns() {
      kind_responses = new List<StoreKindResponse>();
    }

    public StoreAns(List<StoreKindData> store_kind_data, List<NodeId> replicas) {

      kind_responses = new List<StoreKindResponse>();
      foreach (StoreKindData stored_kind in store_kind_data) {
        StoreKindResponse store_kind_response = new StoreKindResponse();
        store_kind_response.kind = stored_kind.Kind;
        store_kind_response.generation_counter = stored_kind.Generation_counter;
        store_kind_response.replicas = new List<NodeId>();
        foreach (NodeId nodeid in replicas) {
          store_kind_response.replicas.Add(nodeid);
        }
        kind_responses.Add(store_kind_response);
      }
    }

    public override uint Dump(BinaryWriter writer) {

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MessageCode.Store_Answer));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)0));

      foreach (StoreKindResponse kind_response in kind_responses) {
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)kind_response.kind));
        writer.Write(IPAddress.HostToNetworkOrder(
          (long)kind_response.generation_counter));

        UInt16 replicasLength = 0;
        foreach (NodeId nodeId in kind_response.replicas) {
          replicasLength += (ushort)nodeId.Digits;
        }
        writer.Write(IPAddress.HostToNetworkOrder((short)replicasLength));
        // write replicas
        foreach (NodeId nodeId in kind_response.replicas) {
          writer.Write(nodeId.Data);
        }
      }
      return 0;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {
      /* try to read the packet as a StoreAns packet */
      try {
        long postBeforeMsg = reader.BaseStream.Position;
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(
          reader.ReadInt16());
        long posBeforeRead = reader.BaseStream.Position;
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder(
          (int)reader.ReadInt32()));

        if (RELOAD_MsgCode == RELOAD_MessageCode.Store_Answer) {
          while (StreamUtil.ReadBytes(posBeforeRead, reader) < message_len) {
            /* Read kind id */
            UInt32 kindId = (UInt32)(IPAddress.NetworkToHostOrder(reader.ReadInt32()));
            /* Read generation */
            UInt64 generation_counter = (UInt64)(IPAddress.NetworkToHostOrder(reader.ReadInt64()));
            /* read length of replicas */
            long posBeforeReplicas = reader.BaseStream.Position;
            UInt16 replicas_lenght = (UInt16)(IPAddress.NetworkToHostOrder(reader.ReadInt16()));
            List<NodeId> replicas = new List<NodeId>();

            /* Read replicas */
            while (StreamUtil.ReadBytes(posBeforeReplicas, reader) < replicas_lenght) {
              NodeId replica = new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS));
              replicas.Add(replica);
            }
            StoreKindResponse store_kind_response = new StoreKindResponse();
            store_kind_response.kind = kindId;
            store_kind_response.generation_counter = generation_counter;
            store_kind_response.replicas = new List<NodeId>();
            store_kind_response.replicas.AddRange(replicas);

            kind_responses.Add(store_kind_response);
          }
        }
        UInt32 totalRead = StreamUtil.ReadBytes(postBeforeMsg, reader);
        reload_msg_size = reload_msg_size - (totalRead + 1); // TODO check whether true
      }
      catch(Exception ex) {
        throw ex;
      }
      return this;
    }
  }

  /// <summary>
  /// The Fetch request retrieves one or more data elements stored at a
  /// given Resource-ID.  A single Fetch request can retrieve multiple
  /// different kinds. see RELOAD base -12 p.92
  /// </summary>
  public class FetchReq : RELOAD_MessageBody {

    private ResourceId resource;
    private List<StoredDataSpecifier> specifiers;

    private UsageManager myManager;

    public FetchReq(ResourceId resourceId, List<StoredDataSpecifier> specifiers,
      UsageManager manager) {
      this.RELOAD_MsgCode = RELOAD_MessageCode.Fetch_Request;
      this.resource = resourceId;
      this.specifiers = new List<StoredDataSpecifier>();
      this.specifiers.AddRange(specifiers);
      myManager = manager;
    }

    public FetchReq(UsageManager manager) {
      specifiers = new List<StoredDataSpecifier>();
      myManager = manager;
    }

    public override uint Dump(BinaryWriter writer) {
      UInt32 length = 0;
      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder(
        (short)RELOAD_MessageCode.Fetch_Request));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));
      /* Write resource id */
      ReloadGlobals.WriteOpaqueValue(writer, resource.Data, 0xFF);
      /* Placeholder Length of specifiers
       * (StoredDataSpecifier specifiers<0..2^16-1>;)*/
      long posBeforSpec = writer.BaseStream.Position;
      writer.Write(IPAddress.HostToNetworkOrder((short)0));

      // Write Fetch Request
      foreach (StoredDataSpecifier specifier in specifiers) {
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)specifier.kindId));

        writer.Write(IPAddress.HostToNetworkOrder(
          (long)specifier.generation));

        long posBeforeIndex = writer.BaseStream.Position;
        writer.Write(IPAddress.HostToNetworkOrder((short)0));
        switch (myManager.GetDataModelfromKindId(specifier.kindId)) {
          case ReloadGlobals.DataModel.SINGLE_VALUE:
            //  case single_value: ;    /* Empty */ see RELOAD base -12 p.93
            break;
          case ReloadGlobals.DataModel.ARRAY:
            foreach (ArrayRange arrayRange in specifier.Indices) {
              writer.Write(IPAddress.HostToNetworkOrder(
                (int)arrayRange.First));
              writer.Write(IPAddress.HostToNetworkOrder(
                (int)arrayRange.Last));
            }
            break;
          case ReloadGlobals.DataModel.DICTIONARY:
            // wildcast
            if (specifier.Keys != null && specifier.Keys.Count == 0) {
              //writer.Write(IPAddress.HostToNetworkOrder(
              //(short)specifier.Keys.Count));
              //length += 2;
              break;
            }
            else {
              foreach (string key in specifier.Keys) {
                writer.Write(IPAddress.HostToNetworkOrder(
                  (short)key.Length));
                writer.Write(Encoding.ASCII.GetBytes(key));
              }
            }
            break;
          default:
            throw new NotSupportedException(String.Format(
              "Kind Id {0} is not supported!", specifier.kindId));
        }
        StreamUtil.WrittenBytesShortExcludeLength(posBeforeIndex, writer);
      }
      UInt16 len = StreamUtil.WrittenBytesShortExcludeLength(posBeforSpec, writer);

      return length;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm,
      BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a FetchReq packet */
      try {
        long posBeforeMsg = reader.BaseStream.Position;
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(
          reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder(
          reader.ReadInt32()));

        Byte res_id_length = reader.ReadByte();
        if (res_id_length == 0)
          throw new System.Exception("Resource ID length == 0!");
        resource = new ResourceId(reader.ReadBytes(res_id_length));
        long posBeforeSpec = reader.BaseStream.Position;
        UInt16 specifiers_length = (UInt16)IPAddress.NetworkToHostOrder(
          reader.ReadInt16());

        while (StreamUtil.ReadBytes(posBeforeSpec, reader) < specifiers_length) {
          UInt32 kindId = (UInt32)IPAddress.NetworkToHostOrder(
            reader.ReadInt32());

          ReloadGlobals.DataModel model = myManager.GetDataModelfromKindId(kindId);

          UInt64 generation = (UInt64)IPAddress.NetworkToHostOrder(
            reader.ReadInt64());

          long posBeforeIndex = reader.BaseStream.Position;
          UInt16 spec_length = (UInt16)IPAddress.NetworkToHostOrder(
            reader.ReadInt16());

          var specifier = new StoredDataSpecifier(myManager);
          switch (model) {
            case ReloadGlobals.DataModel.SINGLE_VALUE:
              break;
            case ReloadGlobals.DataModel.ARRAY:
              List<ArrayRange> arrayRanges = new List<ArrayRange>();
              while (StreamUtil.ReadBytes(posBeforeIndex, reader) <
                spec_length) {
                UInt32 first = (UInt32)IPAddress.NetworkToHostOrder(
                  reader.ReadInt32());
                UInt32 last = (UInt32)IPAddress.NetworkToHostOrder(
                  reader.ReadInt32());
                arrayRanges.Add(new ArrayRange(first, last));
              }
              specifier = new StoredDataSpecifier(arrayRanges, kindId,
                generation, myManager);
              break;
            case ReloadGlobals.DataModel.DICTIONARY:
              List<string> keys = new List<string>();
              if (spec_length == 0) {
                // wildcast
                specifier = new StoredDataSpecifier(keys, kindId,
                  generation, myManager);
              }
              else {
                while (StreamUtil.ReadBytes(posBeforeIndex, reader) <
                  spec_length) {
                  UInt16 key_length = (UInt16)IPAddress.NetworkToHostOrder(
                    reader.ReadInt16());
                  keys.Add(Encoding.ASCII.GetString(reader.ReadBytes((short)key_length),
                    0, key_length));
                }
                specifier = new StoredDataSpecifier(keys, kindId,
                  generation, myManager);
              }
              break;
            default:
              throw new Exception("An error at FetchReq.FromReader()");
          }
          this.specifiers.Add(specifier);
        }
        UInt32 totalRead = StreamUtil.ReadBytes(posBeforeMsg, reader);
        reload_msg_size = reload_msg_size - (totalRead + 1);
      }
      catch (Exception ex) {
        myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          String.Format("FetchReq.FromBytes: {0}", ex.Message));
      }
      return this;
    }

    public ResourceId ResourceId {
      get { return this.resource; }
    }

    public List<StoredDataSpecifier> Specifiers {
      get { return this.specifiers; }
    }
  }

  /// <summary>
  /// The response to a successful Fetch request is a FetchAns message
  /// containing the data requested by the requester.
  /// The FetchAns structure contains a series of FetchKindResponse
  /// structures.  There MUST be one FetchKindResponse element for each
  /// Kind-ID in the request.
  /// </summary>
  public class FetchAns : RELOAD_MessageBody {

    private List<FetchKindResponse> kind_responses;

    private UsageManager myManager;

    public FetchAns(List<FetchKindResponse> kind_responses, UsageManager manager) {
      this.kind_responses = kind_responses;
      myManager = manager;
    }

    public FetchAns(UsageManager manager) {
      kind_responses = new List<FetchKindResponse>();
      myManager = manager;
    }

    public List<FetchKindResponse> KindResponses {
      get { return this.kind_responses; }
    }

    public override uint Dump(BinaryWriter writer) {

      UInt32 length = 0;
      UInt32 kind_responses_length = 0;
      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder(
        (short)RELOAD_MessageCode.Fetch_Answer));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      /* Placeholder for length of all FetchKindResponses
       * FetchKindResponse      kind_responses<0..2^32-1>;
       */
      long posBeforeResp = writer.BaseStream.Position;
      writer.Write(IPAddress.HostToNetworkOrder((int)kind_responses_length));
      /* length itself */

      foreach (FetchKindResponse kind_response in kind_responses) {
        writer.Write(IPAddress.HostToNetworkOrder(
          (int)kind_response.kind));
        writer.Write(IPAddress.HostToNetworkOrder(
          (long)kind_response.generation));
        /* Placeholder for write length of the values
         * StoredData             values<0..2^32-1>;
         */
        long posBeforeSDs = writer.BaseStream.Position;
        writer.Write(IPAddress.HostToNetworkOrder((int)0));
        foreach (StoredData stored_data in kind_response.values) {
          long posBeforeSD = writer.BaseStream.Position;
          writer.Write(IPAddress.HostToNetworkOrder(0));
          writer.Write(IPAddress.HostToNetworkOrder(
            (long)stored_data.StoreageTime));
          writer.Write(IPAddress.HostToNetworkOrder(
            (int)stored_data.LifeTime));
          // Write StoredDataValue meta data
          stored_data.Value.Dump(writer);
          // Write Usage data
          stored_data.Value.GetUsageValue.dump(writer);
          // Write the Signature
          stored_data.Signature.Dump(writer);
          //TODO stored_data.Signature.Dump(writer);
          StreamUtil.WrittenBytesExcludeLength(posBeforeSD, writer);
        }
        StreamUtil.WrittenBytesExcludeLength(posBeforeSDs, writer);
      }
      StreamUtil.WrittenBytesExcludeLength(posBeforeResp, writer);
      return length;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {
      /* try to read the packet as a FetchAns packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(
          reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder(
          reader.ReadInt32()));

        int length = 0;

        if (RELOAD_MsgCode == RELOAD_MessageCode.Fetch_Answer) {
          long posBeforeKR = reader.BaseStream.Position;
          UInt32 kind_responses_length = (UInt32)IPAddress.NetworkToHostOrder(
            (int)reader.ReadInt32());

          while (StreamUtil.ReadBytes(posBeforeKR, reader) <
            kind_responses_length) {
            FetchKindResponse kind_response = new FetchKindResponse();
            UInt32 kind = (UInt32)IPAddress.NetworkToHostOrder(
              reader.ReadInt32());
            UInt64 generation = (UInt64)IPAddress.NetworkToHostOrder(
              reader.ReadInt64());
            // length of all StoredData contained in this FetchKindResponse
            long posBeforeSD = reader.BaseStream.Position;
            UInt32 stored_data_length = (UInt32)IPAddress.NetworkToHostOrder(
              reader.ReadInt32());
            List<StoredData> values = new List<StoredData>();
            // read StoredData
            while (StreamUtil.ReadBytes(posBeforeSD, reader) <
              stored_data_length) {
              // reading properties of StoredData struct
              UInt32 sdLength = (UInt32)(IPAddress.NetworkToHostOrder(
                reader.ReadInt32()));
              UInt64 storage_time = (UInt64)(IPAddress.NetworkToHostOrder(
                reader.ReadInt64()));
              UInt32 lifetime = (UInt32)(IPAddress.NetworkToHostOrder(
                reader.ReadInt32()));

              ReloadGlobals.DataModel data_model = myManager.GetDataModelfromKindId(kind);

              Boolean exists;
              IUsage usage;
              StoredDataValue stored_data_value;

              switch (data_model) {
                case ReloadGlobals.DataModel.SINGLE_VALUE:
                  throw new NotImplementedException(
                    "There is no Usage with Single Value atm");

                case ReloadGlobals.DataModel.ARRAY:
                  UInt32 index = (UInt32)(IPAddress.NetworkToHostOrder(
                    reader.ReadInt32()));
                  exists = (reader.ReadByte() == 0x00 ? false : true);
                  usage = myManager.GetUsageFromReader(rm, reader,
                    reload_msg_size, kind);

                  stored_data_value = new StoredDataValue(index, usage, exists);
                  break;
                case ReloadGlobals.DataModel.DICTIONARY:
                  UInt16 keyLength = (UInt16)(IPAddress.NetworkToHostOrder(
                    reader.ReadInt16()));
                  /*string key = Encoding.ASCII.GetString(
                  reader.ReadBytes(keyLength), 0, keyLength);
                  */
                  string key = BitConverter.ToString(reader.ReadBytes(keyLength)).Replace("-", string.Empty); //--joscha
                  exists = (reader.ReadByte() == 0x00 ? false : true);
                  usage = myManager.GetUsageFromReader(rm, reader,
                    reload_msg_size, kind);
                  stored_data_value = new StoredDataValue(key, usage, exists);
                  break;
                default:
                  throw new NotSupportedException(
                    String.Format("The data_model {0} is not supported",
                    data_model));
              }
              StoredData stored_data = new StoredData(storage_time,
                lifetime, stored_data_value);
              stored_data.Signature = new Signature(myManager.m_ReloadConfig).FromReader(reader, reload_msg_size);
              // TODO Process signature
              values.Add(stored_data);
            } // end read StoredData
            kind_response.kind = kind;
            kind_response.generation = generation;
            kind_response.values = new List<StoredData>();
            kind_response.values.AddRange(values);
            kind_responses.Add(kind_response);
          } // end read FetchKindResponses                   
        }
        reload_msg_size = reload_msg_size - (length + 1);
      }
      catch {
        throw new Exception();
      }
      return this;
    }
  }

  public class AttachReqAns : RELOAD_MessageBody {
    /* The username fragment (from ICE) */
    private Byte[] ufrag = null;    /* 0 - 255 Länge*/
    /* The The ICE password */
    private Byte[] password = null; /* 0 - 255 Länge*/
    /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
    /* 'active': The endpoint will initiate an outgoing connection. 
     * 'passive': The endpoint will accept an incoming connection. 
     * 'actpass': The endpoint is willing to accept an incoming connection or to initiate an outgoing connection. 
     * 'holdconn': The endpoint does not want the connection to be established for the time being.
        Read more: http://www.faqs.org/rfcs/rfc4145.html#ixzz0ftg6pVcg
     */
    private Byte[] role = new System.Text.ASCIIEncoding().GetBytes("actpass");     /* 0 - 255 Länge*/
    public List<IceCandidate> ice_candidates;
    private bool fSendUpdate;

    public bool SendUpdate {
      get { return fSendUpdate; }
    }

    public List<IceCandidate> IPAddressToIceCandidate(IPAddress ip, int portNum) {
      List<IceCandidate> ice_candidates = new List<IceCandidate>();

      if (ip == null)
        return null;

      if (!ReloadGlobals.IPv6_Enabled)
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
          return null;

      /* Setup an TCP candidate first                                 */
      IceCandidate candidate = new IceCandidate(new IpAddressPort(ip.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6_Address : AddressType.IPv4_Address, ip, (UInt16)portNum), Overlay_Link.TLS_TCP_FH_NO_ICE);

      /* when sending an AttachReqAns, form one candidate with a
         priority value of (2^24)*(126)+(2^8)*(65535)+(2^0)*(256-1) that
         specifies the UDP port being listened to and another one with the
         TCP port.                                                    */

      ice_candidates.Add(candidate);

      /* same on UDP with same port number                            */
      IceCandidate candidate2 = new IceCandidate(new IpAddressPort(ip.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6_Address : AddressType.IPv4_Address, ip, (UInt16)portNum), Overlay_Link.DLTS_UDP_SR_NO_ICE);

      ice_candidates.Add(candidate2);

      return ice_candidates;
    }

    public AttachReqAns(Node chordnode, bool req, bool send_update) {
      RELOAD_MsgCode = req ? RELOAD_MessageCode.Attach_Request : RELOAD_MessageCode.Attach_Answer;
      ice_candidates = chordnode.IceCandidates;
      fSendUpdate = send_update;
    }

    public AttachReqAns() {
      ice_candidates = new List<IceCandidate>();
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Code IceCandidate structure */
      if (ice_candidates != null) {
        /* Before writing the message body the message_code is sufficiant */
        writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
        /* Placeholder for length, which will be filled on return */
        writer.Write(IPAddress.HostToNetworkOrder((int)length));

        /* The username fragment (from ICE), we set it to zero length in NO-ICE */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, ufrag, 0xFF);

        /* The The ICE password, we set it to zero length in NO-ICE */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, password, 0xFF);

        /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, role, 0xFF);


        UInt32 ice_length = 0;
        long ice_length_pos = writer.BaseStream.Position;
        writer.Write(IPAddress.HostToNetworkOrder((short)ice_length));

        foreach (IceCandidate candidate in ice_candidates) {
          writer.Write((byte)candidate.addr_port.type);
          ++ice_length;

          switch (candidate.addr_port.type) {
            case AddressType.IPv4_Address:
            case AddressType.IPv6_Address:
              /* IPv4 address is of type uint32    */
              /* IPv6 address is of type uint128   */
              /* length of IpAddressPort structure */
              writer.Write((byte)(candidate.addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort)));
              ++ice_length;
              writer.Write(candidate.addr_port.ipaddr.GetAddressBytes());
              ice_length += (UInt32)candidate.addr_port.ipaddr.GetAddressBytes().Length;
              break;
          }

          /* port is of type uint16 */
          writer.Write(IPAddress.HostToNetworkOrder((short)candidate.addr_port.port));
          ice_length += 2;

          /* write overlay link which is a single byte    */
          writer.Write((byte)candidate.overlay_link);
          ++ice_length;

          /* corresponds to the foundation production. */
          ice_length += ReloadGlobals.WriteOpaqueValue(writer, candidate.foundation, 0xFF);

          /* write priority which is of type uint32 */
          writer.Write(IPAddress.HostToNetworkOrder((int)candidate.priority));
          ice_length += 4;

          /* write candidate type */
          writer.Write((byte)candidate.cand_type);
          ++ice_length;

          switch (candidate.cand_type) {
            case CandType.host:
              /* do nothing */
              break;
            case CandType.prflx:
            case CandType.relay:
            case CandType.srflx:
              switch (candidate.rel_addr_port.type) {
                case AddressType.IPv4_Address:
                case AddressType.IPv6_Address:
                  /* IPv4 address is of type uint32    */
                  /* IPv6 address is of type uint128   */
                  /* length of IpAddressPort structure */
                  writer.Write((byte)(candidate.rel_addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort)));
                  ++ice_length;
                  writer.Write(candidate.rel_addr_port.ipaddr.GetAddressBytes());
                  ice_length += (UInt32)candidate.rel_addr_port.ipaddr.GetAddressBytes().Length;
                  break;
              }
              break;
          }

          long SizeOfIceExtensions = 0;
          // no ice extensions so far -> length null 
          writer.Write(IPAddress.HostToNetworkOrder((short)SizeOfIceExtensions));
          ice_length += 2;

          length += ice_length;
        }

        /* code length of ice candidates */
        long ice_end = writer.BaseStream.Position;
        writer.BaseStream.Seek(ice_length_pos, SeekOrigin.Begin);
        writer.Write(IPAddress.HostToNetworkOrder((short)ice_length));
        writer.BaseStream.Seek(ice_end, SeekOrigin.Begin);

        /* write send update option */
        writer.Write((byte)(fSendUpdate ? 1 : 0));
        ++length;
      }
      return length;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a AttachReqAns packet */
      try {
        byte length;

        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        /* The username fragment (from ICE), we set it to zero length in NO-ICE */
        length = reader.ReadByte();
        if (length != 0)
          ufrag = reader.ReadBytes(length);
        reload_msg_size -= (length + 1);

        /* The The ICE password, we set it to zero length in NO-ICE */
        length = reader.ReadByte();
        if (length != 0)
          password = reader.ReadBytes(length);
        reload_msg_size -= (length + 1);

        /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
        length = reader.ReadByte();
        if (length != 0)
          role = reader.ReadBytes(length);
        reload_msg_size -= (length + 1);

        long ice_length = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());

        reload_msg_size -= (ice_length + 2);

        while (ice_length > 1) //size of bool which follows
                {
          AddressType type = (AddressType)reader.ReadByte();
          --ice_length;
          IPAddress ip;

          length = reader.ReadByte();
          --ice_length;

          switch (type) {
            case AddressType.IPv4_Address:
              if (length != 6)
                return null;
              ip = new IPAddress(reader.ReadBytes(4));
              ice_length -= length;
              break;
            case AddressType.IPv6_Address:
              if (length != 18)
                return null;
              ip = new IPAddress(reader.ReadBytes(16));
              ice_length -= length;
              break;
            default:
              throw new System.Exception(String.Format("Invalid address type {0} in AttachReqAns!", type));
          }

          UInt16 port = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          ice_length -= 2;
          Overlay_Link overlay_link = (Overlay_Link)reader.ReadByte();
          --ice_length;
          IceCandidate candidate = new IceCandidate(new IpAddressPort(type, ip, port), overlay_link);

          int fond_length = reader.ReadByte();
          candidate.foundation = reader.ReadBytes(fond_length);
          ice_length -= (fond_length - 1);

          candidate.priority = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());
          ice_length -= 4;
          candidate.cand_type = (CandType)reader.ReadByte();
          --ice_length;

          switch (candidate.cand_type) {
            case CandType.host:
              /* do nothing */
              break;
            case CandType.prflx:
            case CandType.relay:
            case CandType.srflx:
              type = (AddressType)reader.ReadByte();
              byte rel_length = reader.ReadByte();
              --ice_length;

              switch (type) {
                case AddressType.IPv4_Address:
                  if (rel_length != 6)
                    return null;
                  ip = new IPAddress(reader.ReadBytes(4));
                  ice_length -= rel_length;
                  break;
                case AddressType.IPv6_Address:
                  if (rel_length != 18)
                    return null;
                  ip = new IPAddress(reader.ReadBytes(16));
                  ice_length -= rel_length;
                  break;
                default:
                  throw new System.Exception(String.Format("Invalid rel address type {0} in AttachReqAns!", type));
              }

              port = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
              ice_length -= 2;

              candidate.rel_addr_port = new IpAddressPort(type, ip, port);
              break;
          }

          // now read ice extension length
          long ice_extension_size = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          if (ice_extension_size != 0) {
            //skip ice extension length
            reader.ReadBytes((int)ice_extension_size);
          }
          ice_length -= (ice_extension_size + 2);
          ice_candidates.Add(candidate);
        }

        fSendUpdate = (bool)(reader.ReadByte() == 0 ? false : true);
        --reload_msg_size;
      }
      catch (Exception ex) {
        throw ex;
      }

      return this;
    }
  }

  public class AppAttachReqAns : RELOAD_MessageBody {
    /* The username fragment (from ICE) */
    private Byte[] ufrag = null;    /* 0 - 255 Länge*/
    /* The The ICE password */
    private Byte[] password = null; /* 0 - 255 Länge*/
    /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
    /* 'active': The endpoint will initiate an outgoing connection. 
     * 'passive': The endpoint will accept an incoming connection. 
     * 'actpass': The endpoint is willing to accept an incoming connection or to initiate an outgoing connection. 
     * 'holdconn': The endpoint does not want the connection to be established for the time being.
        Read more: http://www.faqs.org/rfcs/rfc4145.html#ixzz0ftg6pVcg
     */
    private Byte[] role = new System.Text.ASCIIEncoding().GetBytes("actpass");     /* 0 - 255 Länge*/
    public List<IceCandidate> ice_candidates;

    public List<IceCandidate> IPAddressToIceCandidate(IPAddress ip, int portNum) {
      List<IceCandidate> ice_candidates = new List<IceCandidate>();

      if (ip == null)
        return null;

      /* exclude IPv6 addresses for now                              */
      if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        return null;

      /* Setup an TCP candidate first                                 */
      IceCandidate candidate = new IceCandidate(new IpAddressPort(ip.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6_Address : AddressType.IPv4_Address, ip, (UInt16)portNum), Overlay_Link.TLS_TCP_FH_NO_ICE);

      /* when sending an AppAttachReqAns, form one candidate with a
         priority value of (2^24)*(126)+(2^8)*(65535)+(2^0)*(256-1) that
         specifies the UDP port being listened to and another one with the
         TCP port.                                                    */

      ice_candidates.Add(candidate);

      /* same on UDP with same port number                            */
      IceCandidate candidate2 = new IceCandidate(new IpAddressPort(ip.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6_Address : AddressType.IPv4_Address, ip, (UInt16)portNum), Overlay_Link.DLTS_UDP_SR_NO_ICE);

      ice_candidates.Add(candidate2);

      return ice_candidates;
    }

    public AppAttachReqAns(Node chordnode, bool req) {
      RELOAD_MsgCode = req ? RELOAD_MessageCode.App_Attach_Request : RELOAD_MessageCode.App_Attach_Answer;
      ice_candidates = chordnode.IceCandidates;
    }

    public AppAttachReqAns() {
      ice_candidates = new List<IceCandidate>();
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Code IceCandidate structure */
      if (ice_candidates != null) {
        /* Before writing the message body the message_code is sufficiant */
        writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
        /* Placeholder for length, which will be filled on return */
        writer.Write(IPAddress.HostToNetworkOrder((int)length));

        /* The username fragment (from ICE), we set it to zero length in NO-ICE */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, ufrag, 0xFF);

        /* The The ICE password, we set it to zero length in NO-ICE */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, password, 0xFF);

        /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
        length = length + ReloadGlobals.WriteOpaqueValue(writer, role, 0xFF);


        foreach (IceCandidate candidate in ice_candidates) {
          writer.Write((byte)candidate.addr_port.type);
          ++length;

          switch (candidate.addr_port.type) {
            case AddressType.IPv4_Address:
            case AddressType.IPv6_Address:
              /* IPv4 address is of type uint32    */
              /* IPv6 address is of type uint128   */
              /* length of IpAddressPort structure */
              writer.Write((byte)(candidate.addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort)));
              writer.Write(candidate.addr_port.ipaddr.GetAddressBytes());
              length = length + (UInt32)candidate.addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort);
              break;
          }

          /* port is of type uint16 */
          writer.Write(IPAddress.HostToNetworkOrder((short)candidate.addr_port.port));
          /* write overlay link which is a single byte    */
          writer.Write((byte)candidate.overlay_link);

          /* corresponds to the foundation production. */
          length = length + ReloadGlobals.WriteOpaqueValue(writer, candidate.foundation, 0xFF);

          /* write priority which is of type uint32 */
          writer.Write(IPAddress.HostToNetworkOrder((int)candidate.priority));

          /* write candidate type */
          writer.Write((byte)candidate.cand_type);

          switch (candidate.cand_type) {
            case CandType.host:
              /* do nothing */
              break;
            case CandType.prflx:
            case CandType.relay:
            case CandType.srflx:
              switch (candidate.rel_addr_port.type) {
                case AddressType.IPv4_Address:
                case AddressType.IPv6_Address:
                  /* IPv4 address is of type uint32    */
                  /* IPv6 address is of type uint128   */
                  /* length of IpAddressPort structure */
                  writer.Write((byte)(candidate.rel_addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort)));
                  writer.Write(candidate.rel_addr_port.ipaddr.GetAddressBytes());
                  length = length + (UInt32)candidate.rel_addr_port.ipaddr.GetAddressBytes().Length + sizeof(ushort);
                  break;
              }
              break;
          }

          length = length + 8;
        }
      }
      return length;
    }

    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a AppAttachReqAns packet */
      try {
        byte length;

        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder((int)reader.ReadInt32()));

        /* The username fragment (from ICE), we set it to zero length in NO-ICE */
        length = reader.ReadByte();
        if (length != 0)
          ufrag = reader.ReadBytes(length);
        reload_msg_size = reload_msg_size - (length + 1);

        /* The The ICE password, we set it to zero length in NO-ICE */
        length = reader.ReadByte();
        if (length != 0)
          password = reader.ReadBytes(length);
        reload_msg_size = reload_msg_size - (length + 1);

        /* An active/passive/actpass attribute from RFC 4145 [RFC4145]. */
        length = reader.ReadByte();
        if (length != 0)
          role = reader.ReadBytes(length);
        reload_msg_size = reload_msg_size - (length + 1);

        while (reload_msg_size > 0) {
          AddressType type = (AddressType)reader.ReadByte();
          IPAddress ip;

          length = reader.ReadByte();
          --reload_msg_size;


          switch (type) {
            case AddressType.IPv4_Address:
              if (length != 6)
                return null;
              ip = new IPAddress(reader.ReadBytes(4));
              reload_msg_size = reload_msg_size - length;
              break;
            case AddressType.IPv6_Address:
              if (length != 18)
                return null;
              ip = new IPAddress(reader.ReadBytes(16));
              reload_msg_size = reload_msg_size - length;
              break;
            default:
              throw new System.Exception(String.Format("Invalid address type {0} in AppAttachReqAns!", type));
          }

          UInt16 port = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          Overlay_Link overlay_link = (Overlay_Link)reader.ReadByte();
          IceCandidate candidate = new IceCandidate(new IpAddressPort(type, ip, port), overlay_link);

          int fond_length = reader.ReadByte();
          candidate.foundation = reader.ReadBytes(fond_length);
          reload_msg_size = reload_msg_size - fond_length - 1;

          candidate.priority = (UInt32)IPAddress.NetworkToHostOrder(reader.ReadInt32());
          candidate.cand_type = (CandType)reader.ReadByte();

          reload_msg_size = reload_msg_size - 8;

          switch (candidate.cand_type) {
            case CandType.host:
              /* do nothing */
              break;
            case CandType.prflx:
            case CandType.relay:
            case CandType.srflx:
              type = (AddressType)reader.ReadByte();
              byte rel_length = reader.ReadByte();
              --reload_msg_size;

              switch (type) {
                case AddressType.IPv4_Address:
                  if (rel_length != 6)
                    return null;
                  ip = new IPAddress(reader.ReadBytes(4));
                  reload_msg_size = reload_msg_size - length;
                  break;
                case AddressType.IPv6_Address:
                  if (rel_length != 18)
                    return null;
                  ip = new IPAddress(reader.ReadBytes(16));
                  reload_msg_size = reload_msg_size - rel_length;
                  break;
                default:
                  throw new System.Exception(String.Format("Invalid rel address type {0} in AppAttachReqAns!", type));
              }

              port = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

              candidate.rel_addr_port = new IpAddressPort(type, ip, port);
              break;
          }
          ice_candidates.Add(candidate);
        }
      }
      catch (Exception ex) {
        throw ex;
      }

      return this;
    }
  }

  public class UpdateReqAns : RELOAD_MessageBody {
    ChordUpdateType m_type;
    DateTime m_UpTime;
    private RELOAD_ErrorCode m_result;
    private bool m_received_update = false;
    private UInt32 m_TotalSeconds = 0;
    private List<NodeId> m_successors;

    public List<NodeId> Successors {
      get { return m_successors; }
    }

    private List<NodeId> m_predecessors;

    public List<NodeId> Predecessors {
      get { return m_predecessors; }
    }

    public UpdateReqAns() {
      m_received_update = true;
      m_successors = new List<NodeId>();
      m_predecessors = new List<NodeId>();
    }

    public UpdateReqAns(List<NodeId> successors, List<NodeId> predecessors, ChordUpdateType type, DateTime UpTime)
      // call constructor above
      : this() {
      RELOAD_MsgCode = RELOAD_MsgCode = RELOAD_MessageCode.Update_Request;

      m_type = type;
      m_successors = successors;
      m_predecessors = predecessors;

      /* Another validation that we send no more than SUCCESSOR_CACHE_SIZE values 
       * as we store more for internal use
       */
      if (m_successors.Count > ReloadGlobals.SUCCESSOR_CACHE_SIZE)
        m_successors.RemoveRange(ReloadGlobals.SUCCESSOR_CACHE_SIZE, m_successors.Count - ReloadGlobals.SUCCESSOR_CACHE_SIZE);
      if (m_predecessors.Count > ReloadGlobals.SUCCESSOR_CACHE_SIZE)
        m_predecessors.RemoveRange(ReloadGlobals.SUCCESSOR_CACHE_SIZE, m_predecessors.Count - ReloadGlobals.SUCCESSOR_CACHE_SIZE);

      m_UpTime = UpTime;
    }

    public UpdateReqAns(RELOAD_ErrorCode result)
      // call constructor above
      : this() {
      RELOAD_MsgCode = RELOAD_MessageCode.Update_Answer;

      m_result = result;
    }

    public override UInt32 Dump(BinaryWriter writer) {
      UInt32 length = 0;

      /* Before writing the message body the message_code is sufficiant */
      writer.Write(IPAddress.HostToNetworkOrder((short)RELOAD_MsgCode));
      /* Placeholder for length, which will be filled on return */
      writer.Write(IPAddress.HostToNetworkOrder((int)length));

      if (RELOAD_MsgCode == RELOAD_MessageCode.Update_Request) {
        int UpTimeSeconds = (int)(DateTime.Now - m_UpTime).TotalSeconds;
        if (m_received_update ==true)
          writer.Write(IPAddress.HostToNetworkOrder((int)m_TotalSeconds)); //for signature verification
        else
          writer.Write(IPAddress.HostToNetworkOrder((int)UpTimeSeconds));
        length = length + 4;

        writer.Write((Byte)m_type);
        length = length + 1;

        switch (m_type) {
          case ChordUpdateType.full:
          // TODO we do not support RouteQueryReq so far, so we don't need to deliver send the fingertable, which is needed by type full
          case ChordUpdateType.neighbors:
            int iPredCount = m_predecessors.Count();
            writer.Write(IPAddress.HostToNetworkOrder((short)(iPredCount * ReloadGlobals.NODE_ID_DIGITS)));
            length += 2;

            if (iPredCount != 0) {
              length += (UInt32)(iPredCount * ReloadGlobals.NODE_ID_DIGITS);
              for (int i = 0; i < iPredCount; i++)
                /* only publish aproved predecessors, where we got ice candidates from, anything else will cause chaos */
                writer.Write(m_predecessors[i].Data);
            }

            int iSuccCount = m_successors.Count();
            writer.Write(IPAddress.HostToNetworkOrder((short)(iSuccCount * ReloadGlobals.NODE_ID_DIGITS)));
            length += 2;

            if (iSuccCount != 0) {
              length += (UInt32)(iSuccCount * ReloadGlobals.NODE_ID_DIGITS);
              for (int i = 0; i < iSuccCount; i++)
                /* only publish aproved successors, where we got ice candidates from, anything else will cause chaos */
                writer.Write(m_successors[i].Data);
            }
            break;

          case ChordUpdateType.peer_ready:
            break;
        }
      }
      else {
        writer.Write(IPAddress.HostToNetworkOrder((short)m_result));
        length += 2;
      }

      return length;
    }
    public override RELOAD_MessageBody FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size) {

      /* try to read the packet as a UpdateReqAns packet */
      try {
        RELOAD_MsgCode = (RELOAD_MessageCode)(UInt16)IPAddress.NetworkToHostOrder(
          reader.ReadInt16());
        //reload_msg_size -= 2;

        UInt32 message_len = (UInt32)(IPAddress.HostToNetworkOrder(
          reader.ReadInt32()));

        reload_msg_size -= 4;

        m_successors.Clear();
        m_predecessors.Clear();

        if (RELOAD_MsgCode == RELOAD_MessageCode.Update_Request) {
          m_TotalSeconds = (UInt32)IPAddress.NetworkToHostOrder(
            reader.ReadInt32());
          reload_msg_size -= 4;

          m_type = (ChordUpdateType)reader.ReadByte();
          reload_msg_size -= 1;

          int iLengthOfPredecessors = IPAddress.NetworkToHostOrder(
            reader.ReadInt16());

          reload_msg_size -= (iLengthOfPredecessors + 2);

          if (iLengthOfPredecessors > 0) {
            while (iLengthOfPredecessors > 0) {
              m_predecessors.Add(new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS)));
              iLengthOfPredecessors -= ReloadGlobals.NODE_ID_DIGITS;
            }
          }

          int iLengthOfSuccessors = IPAddress.NetworkToHostOrder(reader.ReadInt16());

          reload_msg_size -= (iLengthOfSuccessors + 2);

          if (iLengthOfSuccessors > 0) {
            while (iLengthOfSuccessors > 0) {
              m_successors.Add(new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS)));
              iLengthOfSuccessors -= ReloadGlobals.NODE_ID_DIGITS;
            }
          }
        }
        else {
          UInt16 Result = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
          reload_msg_size -= 2;
        }
      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }
  }
  #endregion
}
