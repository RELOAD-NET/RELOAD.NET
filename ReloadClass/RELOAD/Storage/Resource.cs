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
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.Storage {
  public class Resource {
    ResourceId resId;

    ReloadConfig m_ReloadConfig;

    // Stores all Single_Values for which this node is responsible
    Dictionary<UInt32, StoredData> m_StoredSingleValues =
      new Dictionary<UInt32, StoredData>();

    // Stores all Array Entries for which this node is responsible
    Dictionary<UInt32, Dictionary<UInt32, StoredData>> m_StoredArrays =
      new Dictionary<UInt32, Dictionary<UInt32, StoredData>>();

    // Stores all Dictionary Entries for which this node is responsible
    Dictionary<UInt32, Dictionary<string, StoredData>> m_StoredDictionaries =
      new Dictionary<UInt32, Dictionary<string, StoredData>>();

    /* Stores the generation counter for each kind 
     * key = kind ID, value = generation */
    Dictionary<UInt32, UInt64> generation = new Dictionary<UInt32, UInt64>();

    public Resource(ResourceId resourceId, ReloadConfig config) {
      this.resId = resourceId;
      m_ReloadConfig = config;
    }

    public void AddStoredData(UInt32 kindId, StoredData storedData, UInt64 generation) {

      ReloadGlobals.DataModel data_model = storedData.Value.DataModel;
      if (storedData.Signature == null)
        storedData.SignData(resId, kindId, m_ReloadConfig.AccessController.MyIdentity, m_ReloadConfig); //sign before store
      string residstr = resId.ToString();
      if (!this.generation.ContainsKey(kindId))
        this.generation.Add(kindId, generation); // MUST check generation counter TODO
      else
        this.generation[kindId] = generation;
      switch (data_model) {

        case ReloadGlobals.DataModel.SINGLE_VALUE:
          if (!m_StoredSingleValues.ContainsKey(kindId)) {
            m_StoredSingleValues.Add(kindId, storedData);
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                "Add Key: {0} -> {1}", residstr, storedData.Value.GetUsageValue.ToString()));
          }
          else {
            if (storedData.Value.single_value_entry.exists) {
              m_StoredSingleValues[kindId] = storedData;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                  "Update Key: {0} -> {1}", residstr, storedData.Value.GetUsageValue.ToString()));
            }
            else {
              StoredData nonExistent = new StoredData(storedData.StoreageTime, storedData.LifeTime,
                  new StoredDataValue(null, false));
              m_StoredSingleValues[kindId] = nonExistent;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Delete Key: {0}", residstr));
            }
          }
          break;
        case ReloadGlobals.DataModel.ARRAY:
          var array = new Dictionary<UInt32, StoredData>(); ;
          UInt32 index = storedData.Value.array_entry.index;
          if (!m_StoredArrays.ContainsKey(kindId)) {
            array.Add(index, storedData); // dangerous casting
            m_StoredArrays.Add(kindId, array);
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                "Add Key: {0} -> {1}", residstr, storedData.Value.GetUsageValue.ToString()));
          }
          else {
            array = m_StoredArrays[kindId];
            if (storedData.Value.array_entry.value.exists) {
              array[index] = storedData;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                  "Update Key: {0} -> {1}", kindId, storedData.Value.GetUsageValue.Report()));
            }
            else {
              StoredData nonExistent = new StoredData(storedData.StoreageTime, storedData.LifeTime,
                  new StoredDataValue(storedData.Value.array_entry.index, null, false));
              array[index] = nonExistent;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Delete Key: {0}", residstr));
            }
            m_StoredArrays[kindId] = array;
          }
          break;
        case ReloadGlobals.DataModel.DICTIONARY:
          Dictionary<string, StoredData> dict;
          string key = storedData.Value.dictionary_entry.key;
          if (!m_StoredDictionaries.ContainsKey(kindId)) {
            dict = new Dictionary<string, StoredData>();
            dict.Add(key, storedData);
            m_StoredDictionaries.Add(kindId, dict);
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                "Add Key: {0} -> {1}", residstr, storedData.Value.GetUsageValue.ToString()));
          }
          else {
            dict = m_StoredDictionaries[kindId];
            if (storedData.Value.dictionary_entry.value.exists) {
              dict[key] = storedData;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format(
                  "Update Key: {0} -> {1}", residstr, storedData.Value.GetUsageValue.ToString()));
            }
            else {
              StoredData nonExistent = new StoredData(storedData.StoreageTime, storedData.LifeTime,
                  new StoredDataValue(storedData.Value.dictionary_entry.key, null, false));
              dict[residstr] = nonExistent;
              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_USAGE, String.Format("Delete Key: {0}", residstr));
            }
            m_StoredDictionaries[kindId] = dict;
          }
          break;
        default:
          throw new NotImplementedException(String.Format("Unknown Data Model: {0}", data_model));
      }
    }

    public List<StoredData> StoredData(StoredDataSpecifier specifier) {
      UInt32 kindId = specifier.kindId;
      //String residstr = res_id.ToString();
      if (!m_StoredSingleValues.ContainsKey(kindId) &&
          !m_StoredArrays.ContainsKey(kindId) &&
          !m_StoredDictionaries.ContainsKey(kindId)) {
        return null;
      }

      if (m_StoredSingleValues.ContainsKey(kindId)) {
        throw new NotImplementedException(
          "There are no Usages with Single Data, not Handled!");
      }
      // There is an array for this resouce id
      if (m_StoredArrays.ContainsKey(kindId)) {
        Dictionary<UInt32, StoredData> result = m_StoredArrays[kindId];
        List<StoredData> resultSet = new List<StoredData>();
        foreach (ArrayRange range in specifier.Indices) {
          for (UInt32 i = range.First; i <= range.Last; i++) {
            if (specifier.generation <= generation[specifier.kindId] &&
                result.ContainsKey(i))
              resultSet.Add(result[i]);
          }
        }
        //kindResponse = new FetchKindResponse(,specifier.kindId, specifier.generation, resultSet);
        return resultSet;
      }
      // There is a dictionary for this resource id
      if (m_StoredDictionaries.ContainsKey(kindId)) {
        Dictionary<string, StoredData> result = m_StoredDictionaries[kindId];
        List<StoredData> resultSet = new List<StoredData>();
        if (specifier.Keys.Count == 0) {
          resultSet = new List<StoredData>(result.Values);
        }
        else {
          foreach (string key in specifier.Keys) {
            if (specifier.generation <= generation[specifier.kindId] && result.ContainsKey(key))
              resultSet.Add(result[key]);
          }
        }
        //kindResponse = new FetchKindResponse(specifier.kindId, specifier.generation, resultSet);
        return resultSet;
      }

      // In an obscure failure case...
      throw new ArgumentException("Can't find any values to StoredDataSpecifier!");
    }

    /// <summary>
    /// Returns all stored data of this Resource/Kind pair
    /// </summary>
    /// <returns>List of StoredData objects. Never null, also if no match</returns>
    public List<StoredData> StoredData(UInt32 kindId) {
      List<StoredData> storedData = new List<StoredData>();
      // single values
      if (m_StoredSingleValues.ContainsKey(kindId))
        storedData.Add(m_StoredSingleValues[kindId]);
      // arrays
      if (m_StoredArrays.ContainsKey(kindId))
        storedData.AddRange(m_StoredArrays[kindId].Values);
      // dictionaries
      if (m_StoredDictionaries.ContainsKey(kindId))
        storedData.AddRange(m_StoredDictionaries[kindId].Values);
      return storedData;
    }

    public List<StoredData> StoredData() {

      List<UInt32> kinds = m_ReloadConfig.ThisMachine.UsageManager.SupportedKinds();
      List<StoredData> allStoredData = new List<StoredData>();
      foreach (UInt32 kindId in kinds)
        allStoredData.AddRange(StoredData(kindId));

      return allStoredData;
    }

    public UInt64 GetGeneration(UInt32 kindId) {
      return generation[kindId];
    }

    public ResourceId Id {

      get { return this.resId; }

    }

  }
}
