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

namespace TSystems.RELOAD.Usage {

  public enum STATE {
    JOINING = 1,
    JOINED = 2,
    IDLE = 3,
    UPWALK = 4,
    DOWNWALK = 5,
    LOOKUP,
  }

  public class ReDiR {

    public delegate bool DReDiRLookupCompleted(string nameSpace, NodeId serviceProviderID);

    public delegate bool DReDiRLookupFailed(ResourceId resid);
    
    
    private NodeId m_key;
    private string m_nameSpace;
    private int m_currentLevel;

    private int Lstart;
    private Machine machine;
    private STATE status;


    #region Events

    /// <summary>
    /// Fires if a ReDiRLookup request has finished
    /// </summary>
    public event DReDiRLookupCompleted ReDiRLookupCompleted;

    /// <summary>
    /// Fires if a ReDiRLookup request has failed
    /// </summary>
    public event DReDiRLookupFailed ReDiRLookupFailed;

    #endregion


    public ReDiR(Machine machine, int Lstart = 2) {
      status = STATE.IDLE;
      this.Lstart = Lstart;
      this.machine = machine;
    }

    bool redir_FetchCompleted(List<IUsage> usages) {
      ResourceId resid = null;
      //machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_FetchCompleted usages.Count=" + usages.Count);
      if (usages.Count != 0) {
        List<RedirServiceProviderData> providerList = new List<RedirServiceProviderData>();
        foreach (var usage in usages) {
          if (usage is NoResultUsage) {
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_FetchCompleted: NoResultUsage");
            resid = new ResourceId(usages[0].ResourceName);
            //processFetchResult(resid, null);
            break;
          }
          else {
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_FetchCompleted: " + usage.Report());
            if (usage.CodePoint == Usage_Code_Point.REDIR_SERVICE_PROVIDER) {
              providerList.Add(((RedirServiceProvider)usage).Data);
              resid = new ResourceId(usages[0].ResourceName); //TODO: cleanup
              if (resid != new ResourceId(usage.ResourceName)) {
                throw new System.Exception(String.Format("Invalid ReDiR Result: Different ResourceIds for one fetch"));
              }
            }
          }
        }
        processFetchResult(resid, providerList);
      }
      else {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "redir_FetchCompleted: usages.Count==0");
        resid = new ResourceId(usages[0].ResourceName);
        processFetchResult(resid, null);
      }


      if (status == STATE.IDLE || status == STATE.JOINED) {
        machine.FetchCompleted -= new DFetchCompleted(redir_FetchCompleted);
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("machine.FetchCompleted -= new DFetchCompleted(redir_FetchCompleted);"));
      }
      return true;
    }

    private void deliverResult(ResourceId resid, string nameSpace, NodeId id) {      

      if (nameSpace == null) {
        ReDiRLookupFailed(resid);
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "ReDiR: No Result for " + resid);
      }
      else {
        ReDiRLookupCompleted(nameSpace, id);
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "ReDiR: Result for " + resid + " : nameSpace=" + nameSpace + "ID=" + id.ToString());
      }

    }

    private void processFetchResult(ResourceId resid, List<RedirServiceProviderData> resultList) {
      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR: fetchResult resid=" + resid);
      List<NodeId> ServiceProviderIDs = new List<NodeId>();
      string nameSpace = null;
      if (resultList != null) {
        machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "fetchResult resultList.Count=" + resultList.Count);
        foreach (RedirServiceProviderData result in resultList) {
          ServiceProviderIDs.Add(result.serviceProvider);
          if (nameSpace != null && nameSpace != result.nameSpace)
            throw new System.Exception(String.Format("Invalid ReDiR Fetch: different Namespaces within one fetch"));
          else
            nameSpace = result.nameSpace;
        }
      }

      if (status == STATE.LOOKUP) {
        if (ServiceProviderIDs.Count > 0 && m_currentLevel > 0) {
          ServiceProviderIDs.Sort();

          if (ServiceProviderIDs.Last() < m_key) { // successor of node n is not present in the tree node associated with I(level,n.id) ??
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "successor of node n is not present in the tree node at I(" + m_currentLevel + "," + m_key.ToString() + ")");
            m_currentLevel = m_currentLevel - 1;
            deliverResult(resid, null, null);
            fetch(m_currentLevel);
          }
          else if (ServiceProviderIDs.Count > 1 && ServiceProviderIDs.Last() >= m_key && ServiceProviderIDs.First() <= m_key) {
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "sandwiched in I(" + m_currentLevel + "," + m_key.ToString() + ")");
            m_currentLevel = m_currentLevel + 1;
            deliverResult(resid, null, null);
            fetch(m_currentLevel);
          }
          else {
            foreach (NodeId id in ServiceProviderIDs) {
              machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR " + id);
              if (id >= m_key) {
                machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR closest successor with NodeID=" + id + " found at Level=" + m_currentLevel);
                deliverResult(resid, nameSpace, id);
                status = STATE.IDLE;
                return;
              }
            }
          }
        }
        else if (m_currentLevel == 0) {
          if (ServiceProviderIDs.Count > 0) {
            ServiceProviderIDs.Sort();
            foreach (NodeId id in ServiceProviderIDs) {
              machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR" + id);
              if (id >= m_key) {
                machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR closest successor with NodeID=" + id + " found at Level=" + m_currentLevel);
                deliverResult(resid, nameSpace, id);
                status = STATE.IDLE;
                return;
              }
            }
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR closest successor with NodeID=" + ServiceProviderIDs.First().ToString() + " found at Level=" + m_currentLevel);
            deliverResult(resid, nameSpace, ServiceProviderIDs.First());
            status = STATE.IDLE;
            return;
          }
          else {
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, "REDIR Nothin");
            deliverResult(resid, null, null);
            status = STATE.IDLE;
            return;
          }
        }
        else {
          m_currentLevel = m_currentLevel - 1;
          //deliverResult(resid, null, null);
          fetch(m_currentLevel);
        }

      }
      else if (status != STATE.JOINED) {
        if (status == STATE.UPWALK) {
          ServiceProviderIDs.Sort();

          store(m_currentLevel);

          //check if ServiceProviderID (my ID) is the numerically lowest of highest among the Node-IDs stored under res_id
          if (ServiceProviderIDs.Count == 0 || ServiceProviderIDs.Last() < m_key || ServiceProviderIDs.First() > m_key) {
            if (m_currentLevel > 0) {
              m_currentLevel = m_currentLevel - 1;
              fetch(m_currentLevel);
            }
            else {
              status = STATE.DOWNWALK;
              m_currentLevel = Lstart;
              fetch(m_currentLevel);
            }
          }
          else {
            status = STATE.DOWNWALK;
            m_currentLevel = Lstart;
            fetch(m_currentLevel);
          }
        }
        else if (status == STATE.DOWNWALK) {
          ServiceProviderIDs.Sort();

          if (ServiceProviderIDs.Count == 1 && ServiceProviderIDs[0] == m_key) {
            status = STATE.JOINED;
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("JOINED REDIR"));
            return;
          }

          if (ServiceProviderIDs.Count == 0) {
            store(m_currentLevel);
            status = STATE.JOINED;
            machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("JOINED REDIR"));
            return;
          }
          //check if ServiceProviderID (my ID) is the numerically lowest of highest among the Node-IDs stored under res_id
          if (ServiceProviderIDs.Last() < m_key || ServiceProviderIDs.First() > m_key) {
            // if so: store yourself under res_id
            store(m_currentLevel);
          }
          // and probe the next level
          m_currentLevel = m_currentLevel + 1;
          fetch(m_currentLevel);
        }
      }
    }

    public bool registerService(string nameSpace) {
      join(machine.Topology.LocalNode.Id, nameSpace);
      return true;
    }

    //ReDiR Interface
    public void join(NodeId id, string nameSpace) {

      m_key = id;
      m_currentLevel = Lstart;
      m_nameSpace = nameSpace;

      int nodeNR = responsibleNodeByLevel(m_currentLevel, m_key);

      status = STATE.UPWALK;
      machine.FetchCompleted += new DFetchCompleted(redir_FetchCompleted);
      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("machine.FetchCompleted += new DFetchCompleted(redir_FetchCompleted);"));

      string[] args = new string[] { m_nameSpace + "," + m_currentLevel + "," + nodeNR };
      machine.GatherCommandsInQueue("Fetch", Usage_Code_Point.REDIR_SERVICE_PROVIDER, 0, null, true, args);
      machine.SendCommand("Fetch");
    }

    public void lookupService(string nameSpace) {
      lookup(machine.Topology.LocalNode.Id, nameSpace);
    }

    //ReDiR Interface
    public bool lookup(NodeId key, string nameSpace) {
      m_key = key;
      m_currentLevel = Lstart;
      m_nameSpace = nameSpace;

      status = STATE.LOOKUP;
      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("machine.FetchCompleted += new DFetchCompleted(redir_FetchCompleted);"));
      machine.FetchCompleted += new DFetchCompleted(redir_FetchCompleted);
      fetch(m_currentLevel);

      return true;
    }

    //public bool store(NodeId id, string nameSpace) {
    //  int nodeNR = responsibleNodeByLevel(Lstart, id);

    //  string[] args = new string[] { "TEST" + "," + Lstart + "," + nodeNR, "TEST", Lstart + "", nodeNR + "" };       //in USAGE verlagern?
    //  machine.GatherCommandsInQueue("Store", Usage_Code_Point.REDIR_SERVICE_PROVIDER, 0, null, true, args);
    //  machine.SendCommand("Store");
    //  return false;
    //}

    #region private

    private bool store(int level) {
      int nodeNR = responsibleNodeByLevel(level, m_key);

      string[] args = new string[] { m_nameSpace + "," + level + "," + nodeNR, m_nameSpace, level + "", nodeNR + "" };       //in USAGE verlagern?
      machine.GatherCommandsInQueue("Store", Usage_Code_Point.REDIR_SERVICE_PROVIDER, 0, null, true, args);
      machine.SendCommand("Store");
      return false;
    }
    
    private bool fetch(int level) {
      int nodeNR = responsibleNodeByLevel(level, m_key);
     
      string[] args = new string[] { m_nameSpace + "," + level + "," + nodeNR };
      machine.GatherCommandsInQueue("Fetch", Usage_Code_Point.REDIR_SERVICE_PROVIDER, 0, null, true, args);
      machine.SendCommand("Fetch");
      return false;
    }

    private int responsibleNodeByLevel(int level, NodeId id)    //TODO:DONE?!
    {
      NodeId max = new NodeId();
      max = max.Max();
      NodeId upperbound = new NodeId();
      upperbound = upperbound.Min();

      int i = 0;

      do {
        if (level == 0)
          break; //only one node at level 0

        NodeId temp = (max) >> (level);
        upperbound = upperbound + ((temp)) + 1;

        if (id < upperbound)
          break;

        i++;
      } while (upperbound.Data[0] != 0x00);

      machine.ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_REDIR, String.Format("REDIR: NodeId: " + id.ToString() + " Level: " + level + " => upper bound:  " + upperbound.ToString() + " Node: " + i));
      return i;

    }

    #endregion
  }
}
