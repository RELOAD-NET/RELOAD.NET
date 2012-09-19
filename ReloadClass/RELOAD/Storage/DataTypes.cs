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
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Topology;
using System.IO;
using TSystems.RELOAD.Utils;
using System.Net;


namespace TSystems.RELOAD.Storage {
    /// <summary>
    /// A single-value element is a simple sequence of bytes.  There may be </br>
    /// only one single-value element for each Resource-ID, Kind-ID pair.
    /// A single value element is represented as a DataValue, which contains
    /// the following two elements:
    /// RELOAD base -12 p.83
    /// --alex
    /// </summary>
    public struct DataValue {
        public Boolean exists;
        public IUsage value;
    }

    /// <summary>
    /// An array is a set of opaque values addressed by an integer index.
    /// Arrays are zero based.
    /// RELOAD base -13 p.83
    /// --alex
    /// </summary>
    public struct ArrayEntry {
        public UInt32 index;
        public DataValue value;
    }

    /// <summary>
    ///  A dictionary is a set of opaque values indexed by an opaque key with
    ///  one value for each key.
    ///  RELOAD base -13 p.84
    /// --alex
    /// </summary>
    public struct DictionaryEntry {
        public string key;
        public DataValue value;
    }

    /// <summary>
    /// The protocol currently defines the following data models:
    ///
    /// o  single value
    /// o  array
    /// o  dictionary
    ///
    /// These are represented with the StoredDataValue structure.  The actual
    /// dataModel is known from the kind being stored.
    /// RELOAD base -13 p.83
    /// --alex
    /// </summary>
    public class StoredDataValue {
        public DataValue single_value_entry;
        public ArrayEntry array_entry;
        public DictionaryEntry dictionary_entry;

        /// <summary>
        /// This private Constructor creates instances of StoredDataValue, that carray a single_value_entry
        /// </summary>        
        public StoredDataValue(IUsage value, Boolean exists) {

            single_value_entry = createDataValue(value, exists);
        }

        /// <summary>
        /// This private Constructor creates instances of StoredDataValue, that carray a array_entry
        /// </summary>        
        public StoredDataValue(UInt32 index, IUsage value, Boolean exists) {
            array_entry = new ArrayEntry();
            array_entry.value = createDataValue(value, exists);
            array_entry.index = index;
        }

        /// <summary>
        /// This Constructor creates instances of StoredDataValue, that carray a dictionary_entry
        /// </summary>        
        public StoredDataValue(string key, IUsage value, Boolean exists) {
            dictionary_entry = new DictionaryEntry();
            DataValue dataValue = createDataValue(value, exists);
            dictionary_entry.value = dataValue;
            dictionary_entry.key = key;
        }

        /// <summary>
        /// Return the Usage cointained by this StoredDataValue
        /// </summary>
        public IUsage GetUsageValue {
            get {
                if (single_value_entry.value != null && single_value_entry.exists) {
                    return single_value_entry.value;
                }
                if (array_entry.value.value != null && array_entry.value.exists) {
                    return array_entry.value.value;
                }
                if (dictionary_entry.value.value != null && dictionary_entry.value.exists) {
                    return dictionary_entry.value.value;
                }
                else {
                    // Should be a "non-existing" DataValue
                    return null;
                }
            }
        }

        private DataValue createDataValue(IUsage value, Boolean exists) {

            DataValue dataValue = new DataValue();
            if (value != null && value.Length != 0 && exists) {
                dataValue.value = value;
                dataValue.exists = true;
                return dataValue;
            }
            else {
                dataValue.value = null;
                dataValue.exists = false;
                return dataValue;
            }
        }

        /// <summary>
        /// Unstable! bzw. Uncheckt
        /// </summary>
        public UInt32 Length {
            get {
                UInt32 lenght = 0;
                if (single_value_entry.value != null) {
                    lenght += (UInt32)single_value_entry.value.Length;
                }
                if (array_entry.value.value != null) {
                    lenght += (UInt32)array_entry.value.value.Length;
                    lenght += 4; // index length
                }
                if (dictionary_entry.value.value != null) {
                    lenght += (UInt32)dictionary_entry.value.value.Length;
                    lenght += (UInt32)dictionary_entry.key.Length;
                }
                /*
                 *  A boolean value is either a 1 or a 0.  The max value of 255 indicates
                 *  this is represented as a single byte on the wire.
                 */
                lenght += 1; // length exists
                return lenght;
            }
        }

        /// <summary>
        /// Returns the DataModel enum for this StoredDataValue
        /// </summary>
        public ReloadGlobals.DataModel DataModel {
            get {
                if (single_value_entry.value != null)
                    return ReloadGlobals.DataModel.SINGLE_VALUE;
                if (array_entry.value.value != null)
                    return ReloadGlobals.DataModel.ARRAY;
                if (dictionary_entry.value.value != null)
                    return ReloadGlobals.DataModel.DICTIONARY;
                else
                    throw new NotSupportedException("This DataModel is not supported by this implementation");
            }
        }

        /// <summary>
        /// This method just serializes itself depended of the data model of the contained Usage data.
        /// 
        /// <para>single_value: writes exists value</para>
        /// 
        /// <para>array_entry : writes exits and index values</para>
        /// 
        /// <para>dictionary  : writes exits and key values</para>
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        public UInt32 Dump(BinaryWriter writer) {
            if (single_value_entry.value != null) {
                Byte exists = (byte)(single_value_entry.exists == true ? 1 : 0);
                writer.Write(exists);
                return 1;
            }
            if (array_entry.value.value != null) {
                writer.Write(IPAddress.HostToNetworkOrder((int)array_entry.index));

                Byte exists = (byte)(array_entry.value.exists == true ? 1 : 0);
                writer.Write(exists);

                return 5; // 4 bytes int + 1 byte Boolean
            }
            if (dictionary_entry.value.value != null) {
                //UInt16 key_length = (UInt16)ReloadGlobals.WriteOpaqueValue(writer, Encoding.ASCII.GetBytes(dictionary_entry.key), 0xFFFF);
                UInt16 key_length = (UInt16)ReloadGlobals.WriteOpaqueValue(writer, HexStringConverter.ToByteArray(dictionary_entry.key), 0xFFFF);
                Byte exists = (byte)(dictionary_entry.value.exists == true ? 1 : 0);
                writer.Write(exists);

                return (UInt32)(key_length + 1); // + 1 Byte exists
            }
            else {
                throw new NullReferenceException("This StoredDataValue does not contain a Usage struct!");
            }
        }
    }

    /// <summary>
    /// The basic unit of stored data is a single StoredData structure.
    /// RELOAD base -12 p.80
    /// --mine
    /// </summary>
    public class StoredData {

        #region Properties

        private UInt32 lifetime;
        /// <summary>
        /// Returns the lifetime.
        /// </summary>
        public UInt32 LifeTime {
            get { return this.lifetime; }
        }
        private UInt64 storage_time;
        /// <summary>
        /// Returns the storage time.
        /// </summary>
        public UInt64 StoreageTime {
            get { return storage_time; }
        }

        private UInt32 length;
        /// <summary>
        /// Mok, is 0 everytime! Just for completness
        /// </summary>
        public UInt32 Length {
            get { return length; }
        }

        private StoredDataValue value;
        /// <summary>
        /// The embedded stored data value 
        /// </summary>
        public StoredDataValue Value {
            get { return value; }
            set { if (value != null) this.value = value; }
        }

        private Signature signature;
        /// <summary>
        /// The Signatures of the data.
        /// </summary>
        public Signature Signature {
            get { return signature; }
            set { signature = value; }
        }


        #endregion

        #region Constructors

        /// <summary>
        /// This contructor should be used by the store_data originator. 
        /// Storeage -and lifetime will be set automaticly.
        /// </summary>
        /// <param name="value">The value to be stored</param>
        public StoredData(StoredDataValue value) {
            storage_time = (UInt64)(DateTime.UtcNow - ReloadGlobals.StartOfEpoch).TotalMilliseconds;
            lifetime = ReloadGlobals.DISCO_REGISTRATION_LIFETIME;
            this.value = value;
            signature = null;
            length = 0;
        }

        /// <summary>
        /// This constructor should be used whiel message deserialization.
        /// </summary>
        /// <param name="storage_time"></param>
        /// <param name="lifetime"></param>
        /// <param name="value"></param>
        public StoredData(UInt64 storage_time, UInt32 lifetime, StoredDataValue value) {
            this.storage_time = storage_time;
            this.lifetime = lifetime;
            this.value = value;
            this.signature = null;
            length = 0;
        }

        #endregion

        /// <summary>
        /// Computes the signature of the stored data.
        /// The input to the signature algorithm is:
        ///
        /// resource_id || kind || storage_time || StoredDataValue ||
        /// SignerIdentity
        /// Where || indicates concatenation.
        /// </summary>
        /// <param name="resId"></param>
        /// <param name="kindId"></param>
        public void SignData(ResourceId resId, UInt32 kindId, SignerIdentity id,
          ReloadConfig rc) {
            signature = new Signature(resId, kindId, storage_time,
              value, id, rc);
        }
    }

    /// <summary>
    ///  Each StoreKindData element represents the data to be stored for a
    ///  single Kind-ID.
    ///  RELOAD base -12 p.86
    ///  --mine
    /// </summary>
    public class StoreKindData {

        #region Properties

        private UInt32 kind;
        public UInt32 Kind {
            get { return kind; }
        }

        private UInt64 generation_counter;
        public UInt64 Generation_counter {
            get { return generation_counter; }
        }

        private List<StoredData> values;
        public List<StoredData> Values {
            get { return values; }
        }

        #endregion

        public StoreKindData(UInt32 kindId, UInt64 gen) {
            kind = kindId;
            generation_counter = gen;
            values = new List<StoredData>();
        }

        public StoreKindData(UInt32 kindId, UInt64 generation, params StoredData[] storedData) {
            if (storedData.Length == 0)
                throw new ArgumentNullException("Forgot some arguments?");
            this.kind = kindId;
            this.generation_counter = generation;
            values = new List<StoredData>();
            foreach (StoredData sd in storedData)
                values.Add(sd);
        }

        public void Add(StoredData sd) {
            if (sd.Value.GetUsageValue == null)
                throw new ArgumentNullException("StoredData to add is null!");
            values.Add(sd);
        }

    }

    /// <summary>
    /// If the data model is array, the specifier contains a list of
    /// ArrayRange elements, each of which contains two integers.  The
    /// first integer is the beginning of the range and the second is the
    /// end of the range. 0 is used to indicate the first element and
    /// 0xffffffff is used to indicate the final element.  The first
    /// integer must be less than the second.  While multiple ranges MAY
    /// be specified, they MUST NOT overlap.
    /// see RELOAD base -12 p.94
    /// </summary>
    public struct ArrayRange : IComparable {
        private UInt32 first;
        private UInt32 last;

        public ArrayRange(UInt32 first, UInt32 last) {
            if (first > last)
                throw new ArgumentException(String.Format(
                  "First index {0} > {1} last index", first, last));
            this.first = first;
            this.last = last;
        }

        public UInt32 First {
            get { return first; }
        }

        public UInt32 Last {
            get { return last; }
        }

        /// <summary>
        /// Compares two ArrayRange structs. As They overlay, it throws an Exception.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public int CompareTo(Object inObj) {
            ArrayRange obj = (ArrayRange)inObj;
            if (last > obj.first || first > obj.Last)
                throw new ArgumentException("Array Ranges overlap each other!");

            if (obj.First < first && obj.Last < last)
                return 1;
            else if (obj.First > first && obj.Last > last)
                return -1;
            else
                return 0;

        }
    }

    /// <summary>
    /// Each StoredDataSpecifier specifies a single kind of data to retrieve
    /// and (if appropriate) the subset of values that are to be retrieved.
    /// see RELOAD base -12 p.93
    /// </summary>
    public class StoredDataSpecifier {

        public UInt32 kindId;
        //public ReloadGlobals.DataModel model; --not needed since RELOAD base -13
        public UInt64 generation;
        private UInt16 length;
        // select{dataModel}
        private List<ArrayRange> indices;
        private List<string> keys;

        // not part of RELOAD base spec. but needed for Fetch operation.
        private string resourceName;
        private UsageManager myManager;

        /// <summary>
        /// Use this contructor to create a StoreDataSpecifier that requests a singale value.
        /// </summary>
        /// <param name="kindId">The Kind ID of the requsted Kind</param>
        /// <param name="model">The Data Model this Kind uses</param>
        /// <param name="generation">The generation_counter of this value</param>
        public StoredDataSpecifier(UInt32 kindId, UInt64 generation, UsageManager manager) {
            if (kindId == 0)
                throw new ArgumentNullException("StoredDataSpecifier does not support null or 0 values");
            if (manager.GetDataModelfromKindId(kindId) != ReloadGlobals.DataModel.SINGLE_VALUE)
                throw new ArgumentException("Use this contructor only for single value Fetch requests");
            this.kindId = kindId;
            this.generation = generation;
            length = 0;
            myManager = manager;
        }

        /// <summary>
        /// Use this contructor to create a StoreDataSpecifier that requests an
        /// array Range.
        /// </summary>
        /// <param name="indices">The array ranges you want to fetch</param>
        /// <param name="kindId">The Kind ID of the requsted Kind</param>
        /// <param name="model">The Data Model this Kind uses</param>
        /// <param name="generation">The generation_counter of this value</param>
        public StoredDataSpecifier(List<ArrayRange> indices, UInt32 kindId,
          UInt64 generation, UsageManager manager) {
            if (kindId == 0)
                throw new ArgumentNullException(
                  "StoredDataSpecifier does not support null or 0 values");
            if (manager.GetDataModelfromKindId(kindId) != ReloadGlobals.DataModel.ARRAY)
                throw new ArgumentException(
                  "Use this contructor only for array data Fetch requests");
            if (indices == null)
                throw new ArgumentNullException(
                  "StoredDataSpecifier for array needs at least one ArrayRange!");

            //While multiple ranges MAY be specified, they MUST NOT overlap.
            indices.Sort();
            myManager = manager;
            this.kindId = kindId;
            this.generation = generation;
            this.indices = new List<ArrayRange>();
            this.indices.AddRange(indices);
            foreach (ArrayRange arrayRange in indices) {
                length += (UInt16)8; // 2 times an UInt32        
            }
        }

        /// <summary>
        /// Use this contructor to create a StoreDataSpecifier that requests a dictionary.
        /// </summary>
        /// <param name="keys">The value keys to be fetched. Set this parameter null to create a wildcard fetch.</param>
        /// <param name="kindId">The Kind ID of the requsted Kind</param>
        /// <param name="model">The Data Model this Kind uses</param>
        /// <param name="generation">The generation_counter of this value</param>
        public StoredDataSpecifier(List<string> keys, UInt32 kindId, UInt64 generation, UsageManager manager) {
            if (kindId == 0)
                throw new ArgumentNullException("StoredDataSpecifier does not support null or 0 values");
            if (manager.GetDataModelfromKindId(kindId) != ReloadGlobals.DataModel.DICTIONARY)
                throw new ArgumentException("Use this contructor only for dictionary data Fetch requests");

            this.kindId = kindId;
            this.generation = generation;
            this.keys = keys;
            myManager = manager;

            // wildcast
            if (keys != null || keys.Count == 0) {
                length += 0;
            }
            else {
                foreach (string key in keys) {
                    length += (UInt16)key.Length;
                }
            }
        }

        /// <summary>
        /// Just an initializer! Use only while reading a message from wire.
        /// </summary>
        public StoredDataSpecifier(UsageManager manager) { myManager = manager; }

        /// <summary>
        /// Returns the length of the specifier including its length value
        /// </summary>
        public UInt16 Length {
            get { return length; }
        }

        public List<ArrayRange> Indices {
            get {
                if (myManager.GetDataModelfromKindId(kindId) != ReloadGlobals.DataModel.ARRAY)
                    throw new MemberAccessException("This specifier requests an array data model!");
                return indices;
            }
        }

        public List<string> Keys {
            get {
                if (myManager.GetDataModelfromKindId(kindId) != ReloadGlobals.DataModel.DICTIONARY)
                    throw new MemberAccessException("This specifier requets a dictionary data model!");
                return keys;
            }
        }

        public string ResourceName {
            get { return resourceName; }
            set { resourceName = value; }
        }

    }

}
