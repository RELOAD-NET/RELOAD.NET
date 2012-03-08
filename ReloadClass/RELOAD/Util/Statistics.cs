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
using System.Collections.Generic;
using System.Net;

namespace TSystems.RELOAD.Utils {
  /// <summary>
  /// Statistics 
  /// </summary>
  public class Statistics {
    private volatile ReloadConfig m_ReloadConfig;
    private DateTime m_start_time = DateTime.MinValue;

    public DateTime StartTime {
      get { return m_start_time; }
      set { m_start_time = value; }
    }

    private volatile IForwardLinkManagement m_interface_flm;
    private volatile NodeId m_Successor;

    private List<string> m_KeyList;
    public List<string> KeyList {
      get { return m_KeyList; }
      set { m_KeyList = value; }
    }

    public NodeId Successor {
      get { return m_Successor; }
      set { m_Successor = value; }
    }
    private volatile NodeId m_Predecessor;

    public NodeId Predecessor {
      get { return m_Predecessor; }
      set { m_Predecessor = value; }
    }

    private UInt64 m_bytes_rx = 0;
    public UInt64 BytesRx {
      get { return m_bytes_rx; }
      set {
        m_bytes_rx = value;
        m_interval_bytes_rx += value;
        m_total_bytes_rx += value;
      }
    }

    private UInt64 m_bytes_tx = 0;
    public UInt64 BytesTx {
      get { return m_bytes_tx; }
      set {
        m_bytes_tx = value;
        m_total_bytes_tx += value;
        m_interval_bytes_tx += value;
      }
    }

    private UInt64 m_total_bytes_rx = 0;

    public UInt64 TotalBytesRx {
      get { return m_total_bytes_rx; }
    }
    private UInt64 m_total_bytes_tx = 0;

    public UInt64 TotalBytesTx {
      get { return m_total_bytes_tx; }
    }
    private UInt64 m_interval_bytes_rx = 0;
    private UInt64 m_interval_bytes_tx = 0;

    private Double m_rx_throughput_persec = 0;
    public Double RxThroughputPerSec {
      get { return m_rx_throughput_persec; }
      set { m_rx_throughput_persec = value; }
    }

    private Double m_tx_throughput_persec = 0;
    public Double TxThroughputPerSec {
      get { return m_tx_throughput_persec; }
      set { m_tx_throughput_persec = value; }
    }

    private int m_transmission_errors = 0;
    private int m_connection_errors = 0;
    private int m_retransmissions = 0;

#if false
        private UInt64 m_nr_connect_fail;
        public UInt64 NrConnectFail
        {
            get { return m_nr_connect_fail; }
        }
#endif
    public Statistics(ReloadConfig reloadConfig) {
      m_ReloadConfig = reloadConfig;
      Reset();
    }

    public void Reset() {
      /* Reset statistics */
      m_start_time = DateTime.Now;
      m_bytes_rx = 0;
      m_bytes_tx = 0;
    }

    public void SetParams(IForwardLinkManagement ifc) {
      m_interface_flm = ifc;
    }

    public void IncConnectionError() {
      ++m_connection_errors;
      /*if (m_connection_errors > 50) {
        m_ReloadConfig.ThisMachine.SendCommand("Exit");
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, "Shuted down this node. More than 50 conn errors!");
      }*/
    }

    public void IncTransmissionError() {
      ++m_transmission_errors;
    }

    public void IncRetransmission() {
      ++m_retransmissions;
    }

    public void Reporting() {
      try {
        if (m_ReloadConfig.ReportURL == null)
          m_ReloadConfig.ReportURL = ReloadGlobals.ReportURL;

        if (m_ReloadConfig.ReportURL == "")
          return;

        if (ReloadGlobals.ReportEnabled == true) {
          if (ReloadGlobals.ReportIncludeStatistic) {
            /* Gather statistics */
            if (m_start_time == DateTime.MinValue)
              m_start_time = DateTime.Now;
            else {
              TimeSpan deltaT = DateTime.Now - m_start_time;
              if (deltaT.Ticks > 0) {
                m_rx_throughput_persec = (Double)(m_total_bytes_rx /*m_interval_bytes_rx */* 8) / deltaT.TotalSeconds;
                m_tx_throughput_persec = (Double)(m_total_bytes_tx  /*m_interval_bytes_tx */* 8) / deltaT.TotalSeconds;

                m_interval_bytes_rx = 0;
                m_interval_bytes_tx = 0;
              }
            }
          }

          List<String> Connections = null;
          if (ReloadGlobals.ReportIncludeConnections) {
            if (m_interface_flm != null) {
              Connections = new List<String>();

              foreach (ReloadConnectionTableInfoElement el in m_interface_flm.ConnectionTable) {
                if (el.NodeID != null)
                  Connections.Add(el.NodeID.ToString());
              }
            }
          }
          //if(Connections != null)
          //  m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, Connections.Count.ToString());

          List<NodeId> fingers = new List<NodeId>();
          List<String> fingersReport = new List<string>();
          if (ReloadGlobals.ReportIncludeFingers) {
            if (m_interface_flm != null) {
              Topology.TopologyPlugin.RoutingTable route = m_ReloadConfig.ThisMachine.Topology.routing_table;
              foreach (Topology.TopologyPlugin.RoutingTable.FTableEntry finger in route.FingerTable) {
                if (finger.Successor != null)
                  fingers.Add(finger.Successor);
              }
              fingers = route.removeDuplicates(fingers);
            }
            foreach (NodeId fing in fingers)
              fingersReport.Add(fing.ToString());
          }

          //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_MEASURE, fingers.Count.ToString());

          if (ReloadGlobals.ReportIncludeTopology && m_ReloadConfig.LocalNodeID != null) {
            Boolean isClient = true;

            if (m_ReloadConfig.IsBootstrap)
              isClient = false;

            if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joined
                || m_ReloadConfig.State == ReloadConfig.RELOAD_State.Leave)
              isClient = false;

            TimeSpan tsUpTime = DateTime.Now - m_ReloadConfig.StartTime;
            TimeSpan tsJoinedTime = new TimeSpan();

            if (!isClient && m_ReloadConfig.LastJoinedTime != DateTime.MinValue)
              tsJoinedTime = DateTime.Now - m_ReloadConfig.LastJoinedTime;

            TimeSpan tsConEstabl = new TimeSpan();

            bool test = m_ReloadConfig.IsFocus;

            if (m_ReloadConfig.ConnEstEnd > m_ReloadConfig.ConnEstStart)
              tsConEstabl = m_ReloadConfig.ConnEstEnd - m_ReloadConfig.ConnEstStart;

            string sreport = ReloadGlobals.JSONSerialize(
                             new {
                               Id = m_ReloadConfig.LocalNodeID.ToString(),
                               IP = m_ReloadConfig.LocalNode.IceCandidates[0].addr_port.ipaddr.ToString(),
                               Port = m_ReloadConfig.ListenPort,
                               AOR = m_ReloadConfig.SipUri,
                               Host = ReloadGlobals.HostName,
                               TabPage = m_ReloadConfig.TabPage,
                               Successor = m_Successor == null ? m_ReloadConfig.LocalNodeID.ToString() : m_Successor.ToString(),
                               Predecessor = m_Predecessor == null ? m_ReloadConfig.LocalNodeID.ToString() : m_Predecessor.ToString(),
                               Keys = KeyList,
                               FixedNodes = !ReloadGlobals.SimpleNodeId || ReloadGlobals.TLS ? 0 : System.Math.Pow(2, ReloadGlobals.FINGER_TABLE_ENTRIES),
                               NrDigits = ReloadGlobals.NODE_ID_DIGITS,
                               ConnectionList = Connections,
                               FingerTable = fingersReport,
                               IsClient = isClient,
                               IsBootstrap = m_ReloadConfig.IsBootstrap,
                               IsFocus = m_ReloadConfig.IsFocus,
                               MyFocus = m_ReloadConfig.MyFocus != null ? m_ReloadConfig.MyFocus.ToString() : "",
                               Coordinate = m_ReloadConfig.MyCoordinate.ToString(),
                               TotalBytesRx = TotalBytesRx / 1024,
                               TotalBytesTx = TotalBytesTx / 1024,
                               RxPerSec = (long)RxThroughputPerSec,
                               TxPerSec = (long)TxThroughputPerSec,
                               TransmissionErrors = m_transmission_errors,
                               ConnectionErrors = m_connection_errors,
                               Retransmissions = m_retransmissions,
                               UpTime = string.Format("{0:00}:{1:00}:{2:00}", tsUpTime.TotalHours, tsUpTime.Minutes, tsUpTime.Seconds),
                               JoinedTime = string.Format("{0:00}:{1:00}:{2:00}", tsJoinedTime.TotalHours, tsJoinedTime.Minutes, tsJoinedTime.Seconds),
                               ConnEstablTime = string.Format("{0:00}:{1:00}:{2:00}", tsConEstabl.TotalHours, tsConEstabl.Minutes, tsConEstabl.Seconds)
                             });

            Uri req_uri = new Uri(String.Format("{0}?node{1}={2}", m_ReloadConfig.ReportURL, m_ReloadConfig.LocalNodeID, sreport));
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(req_uri);
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            res.Close();
          }
        }
      }
      catch (Exception ex) {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, ex.Message);
      }
    }
  }
}
