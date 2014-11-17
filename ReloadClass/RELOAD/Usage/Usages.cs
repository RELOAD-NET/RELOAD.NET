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
using System.IO;
using Microsoft.Ccr.Core;
using System.Net;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Enroll;

using Conv = System.Net.IPAddress;
using TSystems.RELOAD.Application;

namespace TSystems.RELOAD.Usage {
  #region Usages

  public enum Usage_Code_Point {
    DISCO,
    SIP_REGISTRATION,
    ACCESS_LIST,
    CERTIFICATE_STORE_BY_NODE,
    CERTIFICATE_STORE_BY_USER,
    REDIR_SERVICE_PROVIDER,
    NULL_USAGE,
            /// <summary>
        /// See <see cref="ImageStoreUsage"/>.
        /// </summary>
        IMAGE_STORE
  }

  public class UsageManager {

    #region Usage Manager

    public Node localNode;    

    private MessageTransport m_Transport;

    public DispatcherQueue m_DispatcherQueue;

    public ReloadConfig m_ReloadConfig;

    private ArrayManager m_ArrayManager;

    public ArrayManager MyArrayManager {
      get { return m_ArrayManager; }
    }

    private Dictionary<Usage_Code_Point, IUsage> usages;

    public Dictionary<Usage_Code_Point, IUsage> RegisteredUsages {
      get { return usages; }
    }    

    /// <summary>
    /// Inits the Usage Manager.
    /// </summary>
    /// <param name="machine">The state machine for RELOAD overlay.</param>
    public void Init(Machine machine) {
      localNode = machine.Topology.LocalNode;
      m_Transport = machine.Transport;
      m_ReloadConfig = machine.ReloadConfig;
      m_DispatcherQueue = machine.ReloadConfig.DispatcherQueue;
      usages = new Dictionary<Usage_Code_Point, IUsage>();
      m_ArrayManager = new ArrayManager(); 
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="usage"></param>
    public void RegisterUsage(IUsage usage) {
      if (usage == null)
        throw new ArgumentNullException("Usage reference is null");
      usages.Add(usage.CodePoint, usage);
    }

    /// <summary>
    /// This class manages the array indexes for storing Kinds of type array.
    /// </summary>
    public class ArrayManager {

      Dictionary<string, UInt32> indices;

      public ArrayManager() {
        indices = new Dictionary<string, uint>();
      }

      public UInt32 CurrentIndex(ResourceId resId, UInt32 kindId) {
        string resourceKindPair = resId.ToString() + kindId.ToString();
        if (!indices.ContainsKey(resourceKindPair))
          indices.Add(resourceKindPair, 0);
        UInt32 index = indices[resourceKindPair];
        indices[resourceKindPair] = ++index;
        return index;
      }
    }

    /// <summary>
    /// Returns the DataModel from a given KindId
    /// </summary>
    /// <param name="kindId">The KindId of the Kind</param>
    /// <returns>A ReloadGlobals.DataModel enum</returns>
    public ReloadGlobals.DataModel GetDataModelfromKindId(UInt32 kindId) {

      foreach (IUsage usage in usages.Values) {
        if (usage.KindId == kindId) {
          return usage.DataModel();
        }
      }
      throw new NotSupportedException(String.Format("Kind ID {0} is not supported!", kindId));
    }

    /// <summary>
    /// Deserializes a Usage object from wire.
    /// </summary>
    /// <param name="reader">The reader. It MUST be desesiralied until the DataValue struct including the Boolean exists value.</param>
    /// <param name="KindId">An UInt32 carrying the Kinde Code Point to this Usage.</param>
    /// <returns></returns>
    public IUsage GetUsageFromReader(ReloadMessage rm, BinaryReader reader, long usage_length, UInt32 kindId) {

      foreach (IUsage usage in usages.Values) {
        if (usage.KindId == kindId)
          return usage.FromReader(rm, reader, usage_length);
      }
      throw new NotSupportedException(String.Format("Kind ID {0} is not supported!", kindId));
    }

    /// <summary>
    /// This method creates Usages. Use this if your intending to Store or Delete Kinds in the Overlay.
    /// If type and arguments are null, it returns an empty usage obejct of the specified Usage
    /// </summary>
    /// <param name="kindId"></param>
    /// <param name="type"></param>
    /// <param name="arguments">argument[0] Resource Name
    /// 
    ///                         If SIP Registration: args[0] = uri | contact prefs
    ///                         If DisCo Registration: args[1] TODO
    ///                         If Access List: args[1] = kindId
    ///                                         args[2] = from_user
    ///                                         args[3] = to_user
    ///                                         args[4] = allow_delegation</param>
    /// <returns></returns>
    public IUsage CreateUsage(Usage_Code_Point usageCode, int? type, params object[] arguments) {

      if (type == null && arguments == null) {
        return usages[usageCode];
      }
      return usages[usageCode].Create(type, arguments);
    }

    internal void AppProcedure(List<FetchKindResponse> responses) {

      var diffUsages = new List<UInt32>();
      var recUsages = new List<IUsage>();
      /* Extracts each Usage. Only a single Intance per Usage */
      foreach (var fkr in responses) {
        List<StoredData> storedDatas = fkr.values;
        foreach (StoredData storedData in storedDatas) {
          IUsage crrUsage = storedData.Value.GetUsageValue;
          /* We alreay got an instance of this usage type? */
          if (!diffUsages.Contains(crrUsage.KindId)) {
            diffUsages.Add(crrUsage.KindId);
            recUsages.Add(storedData.Value.GetUsageValue);
          }
        }
      }
      /* Do the AppProcedure */
      foreach (IUsage usage in recUsages) {
        while (responses.Count > 0)
          usages[usage.CodePoint].AppProcedure(m_Transport, responses);
      }
    }

    public List<UInt32> SupportedKinds() {
      List<UInt32> supportedKinds = new List<UInt32>();
      foreach (IUsage usage in usages.Values)
        supportedKinds.Add(usage.KindId);

      return supportedKinds;
    }

    /// <summary>
    /// Creates a StoredDataSpecifier depending on the requested RELOAD Kind
    /// </summary>
    /// <param name="kindId">The Kind-ID of the requested data</param>
    /// <param name="type">If the data structure allows multiple types, insert here the number of the type. (as in the Usages spec.)</param>
    /// <param name="arguments">If dictionay, the dictionary keys,
    ///                         If array, index[1], idex[2] = first, last array index, 
    ///                         If single value, argument == null</param>
    /// <returns></returns>
    public StoredDataSpecifier createSpecifier(UInt32 kindId, params object[] arguments) {

      ReloadGlobals.DataModel dataModel = GetDataModelfromKindId(kindId);
      StoredDataSpecifier spec = null;
      switch (dataModel) {
        case ReloadGlobals.DataModel.SINGLE_VALUE:
          // There is still no single value Kind
          break;
        case ReloadGlobals.DataModel.ARRAY:
          List<ArrayRange> ranges = new List<ArrayRange>();
          for (int i = 1; i <= arguments.Length - 1; i++) {
              UInt32 first = UInt16.Parse(arguments[i].ToString());
              UInt32 last = UInt16.Parse(arguments[++i].ToString());
            ranges.Add(new ArrayRange(first, last));
          }
          spec = new StoredDataSpecifier(ranges, kindId, 0, this);
          spec.ResourceName = arguments[0].ToString();
          return spec;
        case ReloadGlobals.DataModel.DICTIONARY:
          List<string> keys = new List<string>();
          if (arguments.Count<object>() > 1) {
            foreach (string key in arguments.ToList<object>().GetRange(1, arguments.Length))
              keys.Add(new ResourceId(key).ToString());
          }
          spec = new StoredDataSpecifier(keys, kindId, 0, this);
          spec.ResourceName = arguments[0].ToString();
          return spec;
        default:
          throw new NotSupportedException(String.Format("Data Model {0} is not supported!", dataModel));
      }

      throw new NotImplementedException();
    }
    #endregion
  }

  #region Usage Interface

  /// <summary>
  /// This interface represents a generic super class for RELOAD Usages. Each new Usage MUST implement this interface
  /// </summary>
  public interface IUsage {

    /// <summary>
    /// The returned code point is NOT the real KindId value.
    /// Use the returned Kind_Code_Point to switch() between KindIds
    /// </summary>
    Usage_Code_Point CodePoint { get; }

    /// <summary>
    /// Returns the lenght value.
    /// </summary>
    /// <returns>An UInt32</returns>
    UInt32 Length { get; }


    /// <summary>
    /// Instanciates a new Usage object
    /// </summary>
    /// <param name="type">If this Usages supportes multiple types, annouces
    /// it by setting the type to the type values as defined in the corresponding draft.</param>
    /// <param name="arguments">strings</param>
    /// <returns>A Usage object</returns>
    IUsage Create(int? type, params object[] arguments);

    /// <summary>
    /// This method serializes the StoreKindData into a BinaryWriter.
    /// </summary>
    /// <param name="write">The BinaryWriter to serialize</param>
    /// <returns>The lenght of the serialized bytes</returns>
    UInt32 dump(BinaryWriter write);

    /// <summary>
    /// This method deserializes the StoreKindData for a RELOAD Kind.
    /// </summary>
    /// <param name="rm">Reload message from wire.</param>
    /// <param name="reader">The BinaryReader</param>
    /// <param name="usage_size">Lentgh of the message in bytes.</param>
    /// <returns></returns>
    IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size);

    /// <summary>
    /// Frame a Usage into a StoredDataValue struct.
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="exists"></param>
    /// <returns></returns>
    StoredDataValue Encapsulate(Boolean exists);

    /// <summary>
    /// This method performs the application layer procedure of a Usage.
    /// </summary>
    /// <param name="kindResponse">The Fetch result as FetchKindResponse object.</param>
    void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindResponse);

    /// <summary>
    /// This method is like a toString(). It describes this object with a human-readable string
    /// </summary>
    /// <returns>A string containing information about this Usage</returns>
    string Report();

    /// <summary>
    /// Each Usage needs to specify how the Resource Name is formed that is hashed to form the
    /// Resource-ID where each kind is stored. Reload base -13 p. 32
    /// </summary>
    string ResourceName { get; set; }

    /// <summary>
    /// Returns the name of this Usage as defined in its draft/rfc
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns the Kind code point(s) as defined in Usage.
    /// </summary>
    UInt32 KindId { get; }

    /// <summary>
    /// Returns the data model for a specific Kind this Usages utilizes.
    /// </summary>        
    /// <returns>A ReloadGlobals.DataModel enum</returns>
    ReloadGlobals.DataModel DataModel();
  }

  #endregion

  #region DisCoRegistration

  /// <summary>
  /// o  If the DisCoRegistration is set to "sip_focus_uri", then it
  ///  contains an Address-of-Record (AoR) as an opaque string and opaque
  ///  "coordinates" string, that describes the relative network
  ///  position.
  ///
  /// o  If registration type is set to "sip_focus_node" then it contains
  ///  the Destination list for the peer and an opaque string
  ///  "coordinates" describing the focus' relative network position.
  /// </summary>
  public struct DisCoRegistrationData {
    public string coordinate;
    public NodeId node_id;

    public DisCoRegistrationData(string coordinate, NodeId nodeId) {
      this.coordinate = coordinate;
      this.node_id = nodeId;
    }

    public string Coordinate {
      get { return coordinate; }
    }
  }

  /// <summary>
  /// This class implements the RELOAD Usage for Distributed Conferencing (DisCo)
  /// </summary>
  public class DisCoRegistration : IUsage {

    private string resourceName;
    private UInt16 length;
    private DisCoRegistrationData data;

    private Usage_Code_Point codePoint;
    private UsageManager myManager;

    /// <summary>
    /// This contructor should be taken if you want to create a DisCoRegistration from wire.
    /// </summary>        
    public DisCoRegistration(UsageManager manager) {
      myManager = manager;
      codePoint = Usage_Code_Point.DISCO;
      length = 0;
    }

    /// <summary>
    /// This constructor instanciates a DisCoRegistration containing the Destination List towards the focus and its coordinate value.
    /// </summary>
    /// <param name="coordinate">The focus' relative position in the network.</param>
    /// <param name="dest_list"></param>
    public DisCoRegistration(string resName, string coordinate,
        NodeId nodeId, UsageManager manager) {
      if (coordinate == null || nodeId == null)
        throw new ArgumentNullException("DisCoRegistration does not accept null parameters");
      myManager = manager;
      resourceName = resName;
      length = (UInt16)(resName.Length + 2);
      length += (UInt16)nodeId.Digits;
      length += (UInt16)(coordinate.Length + 2);
      length += 2; // length itself
      data = new DisCoRegistrationData(coordinate, nodeId);
    }

    /// <summary>
    /// Creates new instances of DisCoRegistration
    /// </summary>
    /// <param name="type">Not used, set it -1 for example</param>
    /// <param name="arguments">
    /// arg[0] Must be the conference URI as string
    /// arg[1] Must be the coordiate value as string        
    /// </param>
    /// <returns></returns>
    public IUsage Create(int? type, params object[] arguments) {
      if (arguments == null || arguments.Length < 2)
        throw new ArgumentNullException("Too few arguments");
      if ((string)arguments[0] == "")
        throw new ArgumentException("Resource name can't be empty");
      DisCoRegistration disco = new DisCoRegistration(myManager);
      resourceName = (string)arguments[0];
      disco = new DisCoRegistration((string)arguments[0],
          (string)arguments[1], myManager.localNode.Id, myManager);
      return disco;

    }

    public UInt32 Length {
      get { return this.length; }
    }

    public Usage_Code_Point CodePoint {
      get { return this.codePoint; }
    }

    public DisCoRegistrationData Data {
      get { return this.data; }
    }

    public int NoOfFetchKinds {
      get { return 2; }
    }

    /// <summary>
    /// Serialzes a DisCoRegistration to wire.
    /// </summary>
    /// <param name="writer">A BinaryWriter containing the entire byte
    /// stream</param>
    /// <returns></returns>
    public UInt32 dump(BinaryWriter writer) {
      var Ascii = Encoding.ASCII;
      /* opaque resource_name<0..2^16-1> */
      ReloadGlobals.WriteOpaqueValue(writer,
        Ascii.GetBytes(resourceName), 0xFFFF);
      /* uint16 length */
      writer.Write((UInt16)Conv.HostToNetworkOrder((short)length));
      /* DisCoRegistrationData -> coordinate */
      ReloadGlobals.WriteOpaqueValue(writer,
        Ascii.GetBytes(data.coordinate), 0xFFFF);
      /* DisCoRegistrationData -> node_id */
      writer.Write(data.node_id.Data);

      return length;
    }

    /// <summary>
    /// Deserializes a DisCoRegistration from byte stream.
    /// </summary>
    /// <param name="rm">Not used.</param>
    /// <param name="reader">
    /// The binary reader containing the entire RELOAD message</param>
    /// <param name="usage_size">Not used. DisCoReg has fixed length</param>
    /// <returns></returns>
    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) {
      codePoint = Usage_Code_Point.DISCO;
      UInt16 len = 0; // used for length of opaque values            
      try {
        /* lenght of resource name and resource name */
        len = (UInt16)(Conv.NetworkToHostOrder((short)reader.ReadInt16()));
        resourceName = Encoding.ASCII.GetString(reader.ReadBytes(len), 0, len);
        /* length of rest PDU */
        length = (UInt16)(Conv.NetworkToHostOrder((short)reader.ReadInt16()));
        /* length of coordiate and coordinate */
        len = (UInt16)(Conv.NetworkToHostOrder((short)reader.ReadInt16()));
        string coord = Encoding.UTF8.GetString(reader.ReadBytes(len), 0, len);
        /* node_id of remote peer */
        NodeId nodeId = new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS));
        /* Instanciate DisCo data */
        data = new DisCoRegistrationData(coord, nodeId);
        usage_size = usage_size - (length + 1);
      }
      catch (Exception ex) {
        myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("DisCoRegistration FromBytes(): {0}", ex.Message));
      }
      return this;
    }

    public StoredDataValue Encapsulate(Boolean exists) {
      return new StoredDataValue(myManager.localNode.Id.ToString(), this, exists);
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindRes) {

      UInt32 aclKindId = new AccessList(myManager).KindId;

      var responses = new List<FetchKindResponse>();
      responses.AddRange(kindRes);
      var disCoList = new List<DisCoRegistration>();
      var aclList = new List<AccessList>();
      /* Extract all stored data for this usage */
      var rmIndice = new List<int>();
      foreach (FetchKindResponse fkr in responses) {
        if (fkr.kind == aclKindId) {
          foreach (StoredData sd in fkr.values)
            aclList.Add((AccessList)sd.Value.GetUsageValue);
          kindRes.Remove(fkr);
        }
        if (fkr.kind == KindId) {
          foreach (StoredData sd in fkr.values)
            disCoList.Add((DisCoRegistration)sd.Value.GetUsageValue);
          kindRes.Remove(fkr);
        }
      }

      /* begin AppProcedure */

      // TODO validation of DisCo-Regs with ACL
      /* Choose topologically closest focus */
      if (disCoList.Count > 0) {
        /* MOK for demo */
        //var coordinates = new List<int>();        
        foreach (DisCoRegistration dr in disCoList) {
          int remoteCoords = int.Parse(dr.Data.Coordinate);
          NodeId focusId = dr.data.node_id;
          if (remoteCoords == myManager.m_ReloadConfig.MyCoordinate) {
            myManager.m_ReloadConfig.MyFocus = focusId;
            Arbiter.Activate(myManager.m_DispatcherQueue,
                new IterativeTask<Destination, UInt16>(new Destination(focusId), (UInt16)Application_ID.INVALID /*todo: create application-id*/,
                    transport.AppAttachProcedure));
          }
          else {
            myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING,
              "Focus not in my coords");
          }
        }
      }
    }

    public string Report() {
      return ToString();
    }

    public string ResourceName {
      get { return this.resourceName; }
      set { resourceName = value; }
    }

    public string Name {
      get { return "disco-registration"; }
    }

    public UInt32 KindId {
      get {
        return 4321;
      }
    }

    public ReloadGlobals.DataModel DataModel() {
      return ReloadGlobals.DataModel.DICTIONARY;
    }

    /// <summary>
    /// Compares if two DisCoRegistration are equal.
    /// </summary>
    /// <param name="obj">DisCoRegistration to compare.</param>
    /// <returns>true if all value are equal.</returns>
    public override bool Equals(object obj) {
      if (!(obj is DisCoRegistration))
        return false;
      DisCoRegistration reg = (DisCoRegistration)obj;
      if (this.resourceName != reg.resourceName ||
          this.length != reg.length ||
          this.data.Coordinate != reg.data.Coordinate ||
          this.data.node_id != reg.data.node_id)
        return false;
      return true;
    }
  }

  #endregion

  #region AccessList

  public struct AccessListData {
    public UInt32 kind;
    public string to_user;
    public Boolean allow_delegation;

    public AccessListData(UInt32 kind, string to, Boolean ad) {
      this.kind = kind;
      to_user = to;
      allow_delegation = ad;
    }
  }
  /// <summary>
  /// This class implements the RELOAD Usage for shared resource access
  /// </summary>
  public class AccessList : IUsage {


    private string resource_name;
    private UInt16 length;
    public AccessListData data;

    private Usage_Code_Point codePoint;
    private UsageManager myManager;
    private int hashCode;

    /// <summary>
    /// This contructor instanciates an Access List Kind.
    /// </summary>
    /// <param name="resource_name">The Name of the Resource to be shared.</param>
    /// <param name="kindId">The Kind Id of the Resource to be shared.</param>
    /// <param name="to_user"></param>
    /// <param name="to_user"></param>
    /// <param name="allow_delegation"></param>
    private AccessList(string resource_name, string to_user,
        UInt32 kindId, Boolean allow_delegation, UsageManager manager) {

      if (resource_name == null || kindId == 0 || to_user == null)
        throw new ArgumentNullException("An Access List does not allow null parameter.");
      codePoint = Usage_Code_Point.ACCESS_LIST;
      this.resource_name = resource_name;
      length = (UInt16)(resource_name.Length + 2);
      length += (UInt16)2; // KindId            
      length += (UInt16)(to_user.Length + 2);
      length += (UInt16)1; // Boolean allow_delegation
      length += 2; // the length itself

      data = new AccessListData(kindId, to_user, allow_delegation);

      myManager = manager;
      SetHashCode();
    }

    public AccessList(UsageManager manager) {
      myManager = manager;
      codePoint = Usage_Code_Point.ACCESS_LIST;
      length = 0;
      SetHashCode();
    }

    public IUsage Create(int? type, params object[] arguments) {

      Boolean allowDelegation = (Boolean)arguments[3];

      AccessList accessList = new AccessList((string)arguments[0],
                                             (string)arguments[1],
                                             (UInt32)arguments[2],
                                             allowDelegation,
                                             myManager);
      return accessList;
    }

    public Usage_Code_Point CodePoint {
      get { return this.codePoint; }
    }

    public UInt32 Length {
      get { return this.length; }
    }

    /// <summary>
    /// Serializes the AccessControlList Kind.
    /// </summary>
    /// <param name="writer">Binary writer containing the message byte stream.</param>
    /// <returns>Lenght of PDU.</returns>
    public UInt32 dump(BinaryWriter writer) {
      /* write the resource_name */
      ReloadGlobals.WriteOpaqueValue(writer, Encoding.ASCII.GetBytes(resource_name), 0xFFFF);
      /* write length of rest PDU */
      writer.Write((UInt16)System.Net.IPAddress.HostToNetworkOrder((short)length));
      /* write to_user string*/
      ReloadGlobals.WriteOpaqueValue(writer, Encoding.ASCII.GetBytes(data.to_user), 0xFFFF);
      /* write kindId to be shared */
      writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)data.kind));
      /*write allow_delegation flag */
      writer.Write((byte)(data.allow_delegation ? 1 : 0));
      return length;
    }

    /// <summary>
    /// Deserializes AccessControlList Kind from byte stream.
    /// </summary>
    /// <param name="rm">Not used.</param>
    /// <param name="reader">Binary reader containing message byte stream.</param>
    /// <param name="usage_size">Not used. ACL has fix length.</param>
    /// <returns></returns>
    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) {
      codePoint = Usage_Code_Point.ACCESS_LIST;
      UInt16 len = 0; // for opaque string lengths
      try {
        /* read resource_name*/
        len = (UInt16)(System.Net.IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
        resource_name = Encoding.UTF8.GetString(reader.ReadBytes(len), 0, len);
        /* read length of PDU */
        length = (UInt16)(System.Net.IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
        /* read to_user value */
        len = (UInt16)(System.Net.IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
        string to_uri = Encoding.UTF8.GetString(reader.ReadBytes(len), 0, len);
        /* read kindId to be shared */
        UInt32 kindId = (UInt32)System.Net.IPAddress.NetworkToHostOrder((int)reader.ReadInt32());
        /* read allow_delegation flag */
        Boolean allow_delegation = (reader.ReadByte() == 0 ? false : true);
        /* Create ACL data */
        data = new AccessListData(kindId, to_uri, allow_delegation);

        usage_size = usage_size - (length + 1);

      }
      catch (Exception ex) {
        myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
            String.Format("AccessList FromReader(): {0}", ex.Message));
      }
      SetHashCode();
      return this;
    }

    public StoredDataValue Encapsulate(Boolean exists) {

      if (resource_name == null)
        throw new ArgumentNullException("Access List encapsulation not permitted" +
                                        "without Resource name declaration!");
      UInt32 index = myManager.MyArrayManager.CurrentIndex(
          new ResourceId(resource_name),
          this.KindId);
      return new StoredDataValue(index, this, exists);
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindRes) {
      /* Acutally, the ACL has no appprocedure */
      return;
    }

    public string Report() {
      return ToString();
    }

    public string ResourceName {
      get { return resource_name; }
      set { resource_name = value; }
    }

    public string Name {
      get { return "access-list"; }
    }

    public UInt32 KindId {
      get {
        return 3210;
      }
    }

    public ReloadGlobals.DataModel DataModel() {
      return ReloadGlobals.DataModel.ARRAY;
    }

    /// <summary>
    /// Compare of two ACLs are equal.
    /// </summary>
    /// <param name="obj">ACL to compare.</param>
    /// <returns>true, if all values are equal.</returns>
    public override bool Equals(object obj) {
      if (!(obj is AccessList))
        return false;
      AccessList acl = (AccessList)obj;
      if (this.resource_name != acl.resource_name ||
          this.length != acl.length ||
          this.data.to_user != acl.data.to_user ||
          this.data.kind != acl.data.kind ||
          this.data.allow_delegation != acl.data.allow_delegation)
        return false;
      return true;
    }

    private void SetHashCode() {
      if (resource_name == null) {
        hashCode = base.GetHashCode();
        return;
      }
      hashCode = resource_name.GetHashCode();
      hashCode += length.GetHashCode();
      hashCode += data.to_user.GetHashCode();
      hashCode += data.kind.GetHashCode();
      hashCode += data.allow_delegation.GetHashCode();
    }

    public override int GetHashCode() {
      return hashCode;
    }

  }

  #endregion

  #region SipRegistration

  public struct SipRegistrationData {
    public string sip_uri;
    public string contact_prefs;
    public List<Destination> destination_list;

    public SipRegistrationData(string uri) {
      this.sip_uri = uri;
      contact_prefs = null;
      destination_list = null;
    }

    public SipRegistrationData(string contact_prefs, List<Destination> destination_list) {
      this.contact_prefs = contact_prefs;
      this.destination_list = destination_list;
      this.sip_uri = null;
    }
  }

  public class SipRegistration : IUsage {

    private Usage_Code_Point codePoint;
    private SipRegistrationType type;
    private UInt16 length;
    private SipRegistrationData data;

    private string resourceName;
    private UsageManager myManager;

    /// <summary>
    /// If the registration is of type "sip_registration_uri", then the
    /// contents are an opaque string containing the URI.
    /// </summary>
    /// <param name="sip_uri">The SIP URI to be stored</param>
    private SipRegistration(String sip_uri, UsageManager manager) {
      if (sip_uri == null || sip_uri.Length == 0)
        throw new ArgumentNullException("SIP URI can not be null or size = 0");
      codePoint = Usage_Code_Point.SIP_REGISTRATION;
      type = SipRegistrationType.sip_registration_uri;
      data = new SipRegistrationData(sip_uri);
      length = (UInt16)(sip_uri.Length + 2); // +2 for preceding length value (short)
      myManager = manager;
    }

    private SipRegistration(String contact_prefs, List<Destination> destination_list, UsageManager manager) {
      if (contact_prefs == null || destination_list == null || destination_list.Count == 0)
        throw new ArgumentException("SipRegistration does not allow null parameter or destination list < 1");
      type = SipRegistrationType.sip_registration_route;
      codePoint = Usage_Code_Point.SIP_REGISTRATION;
      length = (UInt16)(contact_prefs.Length + 2);
      length += (UInt16)(ReloadMessage.GetDestListNetLength(destination_list) + 2); // + 2 destList preceeding "length" value
      data = new SipRegistrationData(contact_prefs, destination_list);
      myManager = manager;
    }

    /// <summary>
    /// Use this constructor just to have a SipRegistration object.
    /// </summary>
    public SipRegistration(UsageManager manager) {
      myManager = manager;
      codePoint = Usage_Code_Point.SIP_REGISTRATION;
      //length = 0;
    }

    public IUsage Create(int? type, params object[] arguments) {
      SipRegistration sip;
      string forwardURI = "";
      if (arguments.Length > 0)
        forwardURI = URIanalysis((string)arguments[0]);
      switch (type) {
        case 1: // URI
          sip = new SipRegistration(forwardURI, myManager);
          break;
        case 2: // Destination
          List<Destination> dest_list = new List<Destination>();
          dest_list.Add(new Destination(myManager.localNode.Id));
          sip = new SipRegistration("0.5", dest_list, myManager);
          break;
        default:
          throw new NotSupportedException(
              String.Format("The type {0} is not supported!", type));
      }
      if (arguments.Length > 1)
        sip.resourceName = (string)arguments[1];
      else
        sip.resourceName = myManager.m_ReloadConfig.SipUri;

      return sip;
    }

    public Usage_Code_Point CodePoint {
      get { return this.codePoint; }
    }

    public UInt32 Length {
      get { return this.length; }
    }

    /// <summary>
    /// Returns the SipRegistrationType (uri or route) of this SipRegistration
    /// </summary>
    public SipRegistrationType Type {
      get { return this.type; }
    }

    public SipRegistrationData Data {
      get { return this.data; }
    }

    private string URIanalysis(string sipURI) {
      string ForwardUrl = "";
      try {
        if (sipURI.StartsWith("+")) {
          ReloadConfigResolve res = new ReloadConfigResolve(myManager.m_ReloadConfig);
          ForwardUrl = res.ResolveNaptr(sipURI);
          if (ForwardUrl == null) {
            myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, "DNS Enum fallback to sip uri analysis");
            ForwardUrl = sipURI;
            ForwardUrl = ForwardUrl.TrimStart(' ');
            ForwardUrl = ForwardUrl.Replace(" ", "");
            ForwardUrl = "sip:" + ForwardUrl + "@" + myManager.m_ReloadConfig.OverlayName;
          }
        }
        else if (sipURI.StartsWith("sip:")) {
          ForwardUrl = sipURI;
        }
        else {
          throw new ArgumentException("Unsupported URI format! Must start with, e.g., +49 or sip:");
        }
      }
      catch (Exception e) {
        myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, e.Message);
      }
      return ForwardUrl;
    }

    /// <summary>
    /// This method serializes the datagramm of a SipRegistration.
    /// </summary>
    /// <param name="writer">The writer containing the whole RELOAD message.</param>
    /// <returns>The lenght of the SipRegistration datagramm.</returns>
    public UInt32 dump(BinaryWriter writer) {
      writer.Write((Byte)type);
      //length += 1;
      UInt16 dest_list_length = 0;
      long posBeforeRef = writer.BaseStream.Position;
      /* Placeholder */
      writer.Write((UInt16)IPAddress.HostToNetworkOrder((short)0));
      //length += 2;
      switch (type) {
        case SipRegistrationType.sip_registration_uri:
          ReloadGlobals.WriteOpaqueValue(writer,
            Encoding.ASCII.GetBytes(data.sip_uri), 0xFFFF);
          break;

        case SipRegistrationType.sip_registration_route:
          ReloadGlobals.WriteOpaqueValue(writer, Encoding.ASCII.GetBytes(
            data.contact_prefs), 0xFFFF);
          dest_list_length = (UInt16)ReloadMessage.GetDestListNetLength(
            data.destination_list);
          writer.Write(IPAddress.HostToNetworkOrder((short)dest_list_length));
          ReloadMessage.WriteDestList(writer, data.destination_list);
          break;
      }
      this.length = StreamUtil.WrittenBytesShort(posBeforeRef, writer);
      return length;
    }

    /// <summary>
    /// This method reads deserializes a SipRegistration datagramm from incomming bytes.
    /// </summary>
    /// <param name="rm">The ReloadMessage helper class</param>
    /// <param name="reader">The incomming byte stream reader</param>
    /// <param name="usage_size">Size of the Usage datagramm.</param>
    /// <returns></returns>
    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) {
      try {
        type = (SipRegistrationType)reader.ReadByte();
        this.length = (UInt16)(IPAddress.HostToNetworkOrder(
          (short)reader.ReadInt16()));
        switch (type) {
          case SipRegistrationType.sip_registration_uri:
            UInt16 len = (UInt16)(IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
            data = new SipRegistrationData(Encoding.UTF8.GetString(reader.ReadBytes(len), 0, len));
            break;

          case SipRegistrationType.sip_registration_route:
            len = (UInt16)(IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
            string contact_prefs = Encoding.UTF8.GetString(reader.ReadBytes(len), 0, len);

            len = (UInt16)(IPAddress.HostToNetworkOrder((short)reader.ReadInt16()));
            data = new SipRegistrationData(contact_prefs, rm.ReadDestList(reader, len));
            break;
          default:
            throw new SystemException(String.Format("Invalid SipRegistrationType {0}!", type));
        }

        usage_size = usage_size - (length);

      }
      catch (Exception ex) {
        throw ex;
      }
      return this;
    }

    public StoredDataValue Encapsulate(Boolean exists) {
      if (resourceName == null)
        throw new ArgumentNullException("Cannot encap SipRegistration until ResourceName is null!");
      return new StoredDataValue(myManager.localNode.Id.ToString(), this, exists);
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> fetchKindResponses) {
      /* Select KindResponse returned for this usage */
      FetchKindResponse sip = null;
      var responses = new List<FetchKindResponse>();
      responses.AddRange(fetchKindResponses);      
      foreach (FetchKindResponse fkr in responses ) {
        if (fkr.kind == KindId) {
          sip = fkr;
          fetchKindResponses.Remove(fkr);
        }
      }    

      List<StoredData> storedDatas = sip.values;
      foreach (StoredData storedData in storedDatas) {
        IUsage usage = storedData.Value.GetUsageValue;

        SipRegistration sr = (SipRegistration)usage;
        switch (sr.Type) {
          case SipRegistrationType.sip_registration_uri:
            var keys = new List<string>();
            keys.Add(storedData.Value.dictionary_entry.key);
            StoredDataSpecifier specifier = myManager.createSpecifier(
                sr.KindId, sr.Data.sip_uri);
            var specifiers = new List<StoredDataSpecifier>();
            specifiers.Add(specifier);
            Arbiter.Activate(myManager.m_DispatcherQueue,
                new IterativeTask<string, List<StoredDataSpecifier>>(
                    sr.Data.sip_uri, specifiers, transport.Fetch));
            break;

          case SipRegistrationType.sip_registration_route:  //-- joscha TODO: keep this change local
            //Arbiter.Activate(myManager.m_DispatcherQueue,
            //    new IterativeTask<Destination>(sr.Data.destination_list[0],
            //        transport.AppAttachProcedure));
            break;
        }
      }
    }

    public string Report() {
      return data.sip_uri == null ? data.destination_list[0].destination_data.node_id.ToString() : data.sip_uri;
    }

    public string ResourceName {
      get { return this.resourceName; }
      set { resourceName = value; }
    }

    public string Name {
      get { return "sip-registration"; }
    }

    public UInt32 KindId {
      get { return 1234; }
    }

    public ReloadGlobals.DataModel DataModel() {
      return ReloadGlobals.DataModel.DICTIONARY;
    }
  }

  #endregion

  #region RedirServiceProvider

  public struct RedirServiceProviderData {
    public NodeId serviceProvider;
    public string nameSpace;
    public UInt16 level;
    public UInt16 node;

    public RedirServiceProviderData(NodeId serviceProvider, string nameSpace, UInt16 level, UInt16 node) {
      this.serviceProvider = serviceProvider;
      this.nameSpace = nameSpace;
      this.level = level;
      this.node = node;
    }
  }

  /// <summary>
  /// This class implements the RELOAD Usage for ReDiR
  /// </summary>
  public class RedirServiceProvider : IUsage {

    private Usage_Code_Point codePoint;
    private RedirServiceProviderData data;

    private string resourceName;
    private UsageManager myManager;

    /// <summary>
    /// This contructor should be taken if you want to create a RedirServiceProvider from wire.
    /// </summary>        
    public RedirServiceProvider(UsageManager manager) {
      myManager = manager;
      codePoint = Usage_Code_Point.REDIR_SERVICE_PROVIDER;
    }

    /// <summary>
    /// This constructor instantiates a RedirServiceProvider
    /// </summary>
    /// <param name="serviceProvider">The NodeId of the ServiceProvider.</param>
    /// <param name="resourceName">resourceName is "nameSpace,level,node"</param>
    /// <param name="level">level in Redir Tree</param>
    /// <param name="node">node in Redir Tree</param>
    public RedirServiceProvider(NodeId serviceProvider, string resourceName, string nameSpace, UInt16 level, UInt16 node, UsageManager manager) {
      if (serviceProvider == null || nameSpace == null)
        throw new ArgumentNullException("RedirServiceProvider does not accept null parameters");

      codePoint = Usage_Code_Point.REDIR_SERVICE_PROVIDER;
      data = new RedirServiceProviderData(serviceProvider, nameSpace, level, node);

      myManager = manager;
      this.resourceName = resourceName;
    }

    public IUsage Create(int? type, params object[] arguments) {
      if (arguments.Count() < 4)
        throw new ArgumentException("Not enough arguments! Need 0=key 1=nameSpace, 2=level, 3=node");
      if (arguments[0] == null || arguments[1] == null || arguments[2] == null || arguments[3] == null)
        throw new ArgumentNullException("Not enough arguments! Any of the the arguments is null!");

      RedirServiceProvider serviceProvider = new RedirServiceProvider(myManager.localNode.Id, (string)arguments[0], (string)arguments[1], UInt16.Parse((string)arguments[2]), UInt16.Parse((string)arguments[3]), myManager);
      return serviceProvider;

    }

    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) //REDIR_SERVICE_PROVIDER
    {
      codePoint = Usage_Code_Point.REDIR_SERVICE_PROVIDER;

      UInt16 namespacelength;
      string nameSpace;
      UInt16 level;
      UInt16 node;
      NodeId serviceProviderID;

      try {
        uint length = (UInt32)(System.Net.IPAddress.NetworkToHostOrder((int)reader.ReadInt32()));

        UInt16 serviceProviderID_len = (UInt16)(System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt16()));
        serviceProviderID = new NodeId(reader.ReadBytes(serviceProviderID_len));

        namespacelength = (UInt16)(System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt16()));

        nameSpace = Encoding.ASCII.GetString(reader.ReadBytes(namespacelength)); //ASCII for wireshark dissector

        level = (UInt16)(System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt16()));

        node = (UInt16)(System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt16()));

        string resourceName = (nameSpace + "," + level + "," + node);

        RedirServiceProvider serviceProvider = new RedirServiceProvider(serviceProviderID, resourceName, nameSpace, level, node, myManager);

        return serviceProvider;
      }
      catch (Exception ex) {
        throw ex;
      }
    }

    public UInt32 Length {
      get {
        if (this != null)
          return (
              (uint)ReloadGlobals.NODE_ID_DIGITS +    // ServiceProvider ID
              2 + //level - uint16
              2 + //node - uint16
              2 + //namespace length
              (uint)data.nameSpace.Length
              );
        else
          return 0;
      }
    }

    public Usage_Code_Point CodePoint {
      get { return this.codePoint; }
    }

    public RedirServiceProviderData Data {
      get { return this.data; }
    }

    public UInt32 dump(BinaryWriter writer)     // RedirServiceProvider
    {
      uint length_new = 0;

      long DataValueLengthPosition = writer.BaseStream.Position;
      writer.Write((UInt32)System.Net.IPAddress.HostToNetworkOrder((int)0)); //is filled later
      length_new += 4;

      length_new += ReloadGlobals.WriteOpaqueValue(writer, data.serviceProvider.Data, 0xFFFF);
      length_new += (ushort)ReloadGlobals.WriteOpaqueValue(writer, Encoding.ASCII.GetBytes(data.nameSpace), 0xFFFF);

      writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)data.level));
      length_new += 2;
      writer.Write(System.Net.IPAddress.HostToNetworkOrder((short)data.node));
      length_new += 2;

      long DataValueEndPosition = writer.BaseStream.Position;
      writer.BaseStream.Seek(DataValueLengthPosition, SeekOrigin.Begin);
      writer.Write(System.Net.IPAddress.HostToNetworkOrder((int)(DataValueEndPosition - DataValueLengthPosition - 4)));
      writer.BaseStream.Seek(DataValueEndPosition, SeekOrigin.Begin);

      return length_new;
    }

    public StoredDataValue Encapsulate(Boolean exists) {
      return new StoredDataValue(myManager.localNode.Id.ToString(), this, exists);
    }

    public void AppProcedure(MessageTransport transport, List<FetchKindResponse> kindRes)   // TODO: new AppProcedure
    {
      //throw new ArgumentException(String.Format("TODO: new AppProcedure"));
      kindRes.Clear();
      //foreach (FetchKindResponse fkr in kindRes)
      //kindRes.Remove(fkr);
    }

    public void AppProcedure(FetchKindResponse kindRes, MessageTransport transport) {
      myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppProcedure: ");

      List<RedirServiceProviderData> ProviderList = new List<RedirServiceProviderData>();
      List<StoredData> storedDatas = kindRes.values;
      RedirServiceProvider provider = null;
      foreach (StoredData storedData in storedDatas) {
        IUsage usage = storedData.Value.GetUsageValue;

        provider = (RedirServiceProvider)usage;
        ProviderList.Add(provider.Data);
        myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AppProcedure: " + provider.Report());
      }

      //if (myManager != null && myManager.m_ReloadConfig.callback != null && provider != null)
      //    myManager.m_ReloadConfig.callback(new ResourceId(provider.ResourceName), ProviderList);    //TODO: provider.ResourceName MUST (should) be the same for all providers in ProviderList
    }

    public string Report() {
      return ("ServiceProviderID " + data.serviceProvider.ToString() + " NameSpace " + data.nameSpace + " Node: " + data.node + " Level: " + data.level);

    }

    public string ResourceName {
      get { return this.resourceName; }
      set { resourceName = value; }
    }

    public string Name {
      get { return "RedirServiceProvider"; }
    }

    public UInt32[] Kinds {
      get {
        UInt32 REDIR = 104;
        return new UInt32[] { REDIR };
      }
    }

    public ReloadGlobals.DataModel DataModel(UInt32 kindId) {
      if (kindId == ReloadGlobals.REDIR_KIND_ID)
        return ReloadGlobals.DataModel.DICTIONARY;

      throw new ArgumentException(String.Format("KindId {0} not known by RedirServiceProvider", kindId));
    }

    public UInt32 KindId {
      get { return 104; }
    }

    public ReloadGlobals.DataModel DataModel() {
      return ReloadGlobals.DataModel.DICTIONARY;
    }

  }

  #endregion

  #region CertificateStore

  public class CertificateStore : IUsage {

    Usage_Code_Point codePoint;
    UsageManager myManager;
    string username;
    NodeId localNodeId;
    //string certificate;
    byte[] certificate;
    private string resourceName;
    bool byNode;
    UInt32 length;

   /// <summary>
    /// This contructor should be taken if you want to create a RedirServiceProvider from wire.
    /// </summary>        
    public CertificateStore(bool bnode, UsageManager manager)
    {
      codePoint = bnode ? Usage_Code_Point.CERTIFICATE_STORE_BY_NODE : Usage_Code_Point.CERTIFICATE_STORE_BY_USER;
      byNode = bnode;
      myManager = manager;
    }

    public Usage_Code_Point CodePoint {
      get { return codePoint; }
    }

    public uint Length {
      get { return length; }
    }

    /// <summary>
    /// Creates new Instances of the Certificate Usage.
    /// </summary>
    /// <param name="type">0 = CERTIFICATE_BY_NODE, 1 = CERTIFICATE_BY_USER</param>
    /// <param name="arguments">args[0]= username, args[1] = node-id, args[2]=certificate as string</param>
    /// <returns></returns>
    public IUsage Create(int? type, params object[] arguments) {
      if (arguments.Count() < 3)
        throw new ArgumentException("Not enough arguments! Need 0=username, 1=node-id, 2=certificate");
      if (arguments[0] == null || arguments[1] == null || arguments[2] == null)
        throw new ArgumentNullException("Not enough arguments! Any of the the arguments is null!");

      resourceName = (string)arguments[0];
      length += (UInt32)resourceName.Length;

      username = (string)arguments[1];
      length += (UInt32)username.Length;
      localNodeId = (NodeId)arguments[2];
      length += (UInt32)localNodeId.Digits;
      certificate = (byte[])arguments[3];
      length += (UInt32)certificate.Length;

      return this;
    }

    public uint dump(BinaryWriter writer) { 
      //throw new NotImplementedException("dump");
        // --arc
        var ASCII = Encoding.ASCII;
        const ulong MAX_VALUE = 0xFFFFFFFF;

        ReloadGlobals.WriteOpaqueValue(writer, ASCII.GetBytes(resourceName), MAX_VALUE); // ResourceName

        ReloadGlobals.WriteOpaqueValue(writer, ASCII.GetBytes(username), MAX_VALUE); // username
        var deb = myManager.localNode.Id;
        ReloadGlobals.WriteOpaqueValue(writer, localNodeId.Data, MAX_VALUE); // localNodeId
        ReloadGlobals.WriteOpaqueValue(writer, certificate, MAX_VALUE); // certificate

        return Length;
    }

    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size)
    {
        // --arc
        bool bnode = codePoint == Usage_Code_Point.CERTIFICATE_STORE_BY_NODE;
        CertificateStore result = new CertificateStore(bnode, myManager);
        var ASCII = Encoding.ASCII;
        var bytesCount = 0;

        try
        {
            bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            result.resourceName = ASCII.GetString(reader.ReadBytes(bytesCount), 0, bytesCount); // ResourceName

            bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            result.username = ASCII.GetString(reader.ReadBytes(bytesCount), 0, bytesCount); // username

            bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            result.localNodeId = new NodeId(reader.ReadBytes(bytesCount)); // localNodeId

            bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            result.certificate = reader.ReadBytes(bytesCount); // certificate
        }
        catch (Exception e)
        {
            myManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                String.Format("CertificateStore Usage FromReader(): {0}", e.Message));
        }

        // Compute the total usage length
        result.length = 0
            + (uint)(result.ResourceName.Length + 4)
            + (uint)(result.username.Length + 4)
            + (uint)(result.localNodeId.Data.Length + 4)
            + (uint)(result.certificate.Length + 4);

        return result;

      //try
      //{
      //  long posBefore = reader.BaseStream.Position;
      //  var type = (CertificateType)reader.ReadByte();
      //  var unknown = reader.ReadByte();
      //  UInt16 len = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      //  //TElX509Certificate cert = new TElX509Certificate();
      //  //Byte[] bcert = reader.ReadBytes(len);
      //  //cert.LoadFromBuffer(bcert);
      //  long posAfter = reader.BaseStream.Position;

      //  usage_size = usage_size - (posAfter - posBefore);
      //  length = (uint)(posAfter - posBefore); //TK not sure if this is correct
      //}
      //catch (Exception ex)
      //{
      //  throw ex;
      //}
      //return this;
    }

    public StoredDataValue Encapsulate(bool exists) {
      //throw new NotImplementedException("Encapsulate");

        // --arc
        // TODO: how to determine index for array value here? For now always choose index 0
        uint index = 0;
        return new StoredDataValue(index, this, exists); 
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindResponse) {
      throw new NotImplementedException("AppProcedure");
    }

    public string Report()
    {
      return ToString();
    }

    public string ResourceName
    {
      get { return this.resourceName; }
      set { resourceName = value; }
    }

    public string Name {
      get { return byNode ? "certificate_store_by_node" : "certificate_store_by_user"; }
    }

    public UInt32 KindId {
      get { return (UInt32)(byNode ? 3 : 4); }
    }

    public ReloadGlobals.DataModel DataModel(){
      return byNode ? ReloadGlobals.CERTIFICATE_BY_NODE_DATA_MODEL : ReloadGlobals.CERTIFICATE_BY_USER_DATA_MODEL;
    }

  }

  #endregion

  #region NullUsage

  public class NullUsage : IUsage {


    public NullUsage() { }

    public Usage_Code_Point CodePoint {
      get { return Usage_Code_Point.NULL_USAGE; }
    }

    public uint Length {
      get { throw new NotImplementedException(); }
    }

    public IUsage Create(int? type, params object[] arguments) {
      throw new NotImplementedException();
    }

    public uint dump(BinaryWriter write) {
      throw new NotImplementedException();
    }

    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) {
      throw new NotImplementedException();
    }

    public StoredDataValue Encapsulate(bool exists) {
      throw new NotImplementedException();
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindResponse) {
      throw new NotImplementedException();
    }

    public string Report() {
      throw new NotImplementedException();
    }

    public string ResourceName {
      get {
        throw new NotImplementedException();
      }
      set {
        throw new NotImplementedException();
      }
    }

    public string Name {
      get { throw new NotImplementedException(); }
    }

    public uint KindId {
      get { throw new NotImplementedException(); }
    }

    public ReloadGlobals.DataModel DataModel() {
      throw new NotImplementedException();
    }
  }

  #endregion

  #region NoResultUsage
  //This usage is used to signal a failed fetch for ResourceName
  public class NoResultUsage : IUsage {

    private string resourceName;

    public NoResultUsage(string resourceName) { this.resourceName = resourceName; }

    public Usage_Code_Point CodePoint {
      get { return Usage_Code_Point.NULL_USAGE; }
    }

    public uint Length {
      get { throw new NotImplementedException(); }
    }

    public IUsage Create(int? type, params object[] arguments) {
      throw new NotImplementedException();
    }

    public uint dump(BinaryWriter write) {
      throw new NotImplementedException();
    }

    public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long usage_size) {
      throw new NotImplementedException();
    }

    public StoredDataValue Encapsulate(bool exists) {
      throw new NotImplementedException();
    }

    public void AppProcedure(MessageTransport transport,
      List<FetchKindResponse> kindResponse) {
      throw new NotImplementedException();
    }

    public string Report() {
      throw new NotImplementedException();
    }

    public string ResourceName {
      get { return this.resourceName; }
      set { resourceName = value; }
    }

    public string Name {
      get { throw new NotImplementedException(); }
    }

    public uint KindId {
      get { throw new NotImplementedException(); }
    }

    public ReloadGlobals.DataModel DataModel() {
      throw new NotImplementedException();
    }
  }

  #endregion

  #endregion
}
