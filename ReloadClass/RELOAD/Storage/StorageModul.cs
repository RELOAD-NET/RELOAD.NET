using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.Storage {
  /// <summary>
  /// 
  /// </summary>
  public class StorageModul {

    Dictionary<string, Resource> resources;
    ReloadConfig s_ReloadConfig;



    public delegate void DResourceStored(ResourceId resId, StoreKindData kindData);
    /// <summary>
    /// Fires if an Resource has been stored
    /// </summary>
    public event DResourceStored ResourceStored;

    public StorageModul(ReloadConfig config) {
      s_ReloadConfig = config;
      resources = new Dictionary<string, Resource>();
    }

    public void Store(ResourceId resId, StoreKindData kindData) {
      string resourceId = resId.ToString();
      lock (resources) {
        if (!resources.ContainsKey(resourceId)) {
          resources.Add(resourceId, new Resource(resId, s_ReloadConfig));
        }
        Resource resource = resources[resourceId];
        foreach (StoredData storedData in kindData.Values) {
          resource.AddStoredData(kindData.Kind, storedData, kindData.Generation_counter);
        }
       ResourceStored(resId, kindData);
      }
    }

    /// <summary>
    /// Returns FetchKindResponse structs for a given ResourceId and Specifier
    /// </summary>
    /// <param name="resId">The ResourceId</param>
    /// <param name="specifier">The StoredDataSpecifier</param>
    /// <param name="fetchKindResponse">The Fetch result als FetchKindResonse</param>
    /// <returns>true, if value found</returns>
    public bool Fetch(ResourceId resId, StoredDataSpecifier specifier,
      out FetchKindResponse fetchKindResponse) {

      string resouceId = resId.ToString();
      lock (resources) {
        if (!resources.ContainsKey(resouceId)) {
          fetchKindResponse = new FetchKindResponse();
          return false;
        }
        Resource resource = resources[resouceId];
        List<StoredData> machtes = resource.StoredData(specifier);
        if (machtes != null && machtes.Count >= 0) {
          fetchKindResponse = new FetchKindResponse(
            specifier.kindId, resource.GetGeneration(specifier.kindId), machtes);
          return true;
        }
      }
      fetchKindResponse = new FetchKindResponse();
      return false;
    }

    /// <summary>
    /// Executing this method removes all expired (lifetime <= 0) from this peer
    /// </summary>
    public void RemoveExpired() {

      UInt64 now_time = (UInt64)(DateTime.UtcNow - ReloadGlobals.StartOfEpoch).TotalMilliseconds;
      /*foreach (Resource resource in resources.Values) {
        foreach (StoredData data in resource.StoredData()) {
          //sr = m_topology.StoredValues[StoredKey]; -- old

          /* not sure why not remove replicas? --alex
          if (sr.replica != 0)
              //do not delete replicas                    
              continue;
            

          if (now_time - data.StoreageTime > data.LifeTime * 1000) {
            s_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Delete Key {0} which is expired", resource.Id));
            Remove(resource.Id.ToString());
            continue;
          }
        }
      }*/
    }


    /// <summary>
    /// Removes a Resource at the resourceId parameter
    /// </summary>
    /// <param name="resourceId">The Resource Id of the Resource to be removed</param>
    public void Remove(string resourceId) {
      resources.Remove(resourceId);
    }

    /// <summary>
    /// Returns the resource for a given resource Id
    /// </summary>
    /// <param name="resourceId">A string containing the resourceId</param>
    /// <returns>A List of StoreKindData of this Resource</returns>
    public List<StoreKindData> GetStoreKindData(string resourceId) {
      if (resources.ContainsKey(resourceId)) {
        List<StoreKindData> storeKindData = new List<StoreKindData>();
        foreach (UInt32 kindId in s_ReloadConfig.ThisMachine.UsageManager.SupportedKinds()) {
          List<StoredData> storedData;
          if ((storedData = resources[resourceId].StoredData(kindId)).Count != 0)
            storeKindData.Add(new StoreKindData(kindId, resources[resourceId].GetGeneration(kindId), storedData.ToArray()));
        }
        return storeKindData;
      }
      return null;
    }

    /// <summary>
    /// Returns the current generation counter for a given Resource/Kind pair
    /// </summary>
    /// <param name="id">The resourceId</param>
    /// <param name="kindId"></param>
    /// <returns>The generation counter as UInt64</returns>
    public UInt64 GetGeneration(ResourceId id, UInt32 kindId) {

      if (resources.ContainsKey(id.ToString()))
        return resources[id.ToString()].GetGeneration(kindId);

      throw new KeyNotFoundException(String.Format("No Resouce found for Id {0}", id.ToString()));
    }

    /// <summary>
    /// Returns all keys that are stored
    /// </summary>
    public List<string> StoredKeys {
      get {
        if (resources.Count > 0)
          return new List<string>(resources.Keys);
        else
          return null;
      }
    }

    /// <summary>
    /// Returns ALL stored data by this peer
    /// </summary>
    public List<StoreKindData> StoredValues {
      get {
        if (resources.Count > 0) {
          List<StoreKindData> storedData = new List<StoreKindData>();
          foreach (UInt32 kindId in s_ReloadConfig.ThisMachine.UsageManager.SupportedKinds()) {
            foreach (Resource resource in resources.Values) {
              if (resource.StoredData(kindId).Count != 0)
                storedData.Add(new StoreKindData(
                    kindId, resource.GetGeneration(kindId), resource.StoredData(kindId).ToArray()));
            }
          }
          return storedData;
        }
        else {
          return null;
        }
      }
    }
  }
}
