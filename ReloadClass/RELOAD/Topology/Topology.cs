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
using System.Text;
using System.Net;
using System.ComponentModel;
using System.Configuration;
using System.Net.Sockets;
using System.Linq;
using TSystems.RELOAD.ForwardAndLinkManagement;
using Microsoft.Ccr.Core;
using TSystems.RELOAD;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Transport;

namespace TSystems.RELOAD.Topology {

    /// <summary>
    /// RELOAD main instance protocol routines
    /// </summary>
    public class TopologyPlugin {
        private DispatcherQueue m_DispatcherQueue;
        Dictionary<string, SipRegistration> m_StoredValues = new Dictionary<string, SipRegistration>();

        private Dictionary<string, SipRegistration> StoredValues {
            get { return m_StoredValues; }
            set { m_StoredValues = value; }
        }

        // REPLICATEST
        // Replica List, contains keys of replicas
        private List<string> m_replicas = new List<string>();

        public List<string> Replicas
        {
            get { return m_replicas; }
            set { m_replicas = value; }
        }

        private StorageModul storage;
        /// <summary>
        /// Returns the storage modul of this peer
        /// </summary>
        public StorageModul Storage {

            get { return storage; }

        }

        private NodeId m_id;
        public NodeId Id {
            get { return m_id; }
            set { m_id = value; }
        }


        // list of predecessors and successors
        // chord-reload specifies finger[0] as the peer 180 degrees aroun the ring from the peer

        public class RoutingTable {
            private ReloadConfig m_ReloadConfig = null;
            private MessageTransport m_transport = null;

            DispatcherQueue m_DispatcherQueue;
            private IForwardLinkManagement m_flm = null;

            public class RTableEntry {
                public List<IceCandidate> icecandidates;
                public DateTime dtLastSuccessfullPing;
                public NodeState nodestate;
                public Node node;
                public bool pinging;
                public bool wait_for_join_answ;
            }

            public class FTableEntry {
                public ResourceId Finger;
                public NodeId Successor;
                public DateTime dtLastSuccessfullFinger;
                public NodeState nodestate;
                public bool pinging;
                public bool valid;
            }

            Dictionary<string, RTableEntry> m_RtTable = new Dictionary<string, RTableEntry>();
            Dictionary<string, NodeId> m_LearnedFromTable = new Dictionary<string, NodeId>();
            Dictionary<string, UpdateReqAns> m_NeighborInfoTable = new Dictionary<string, UpdateReqAns>();

            // A list of all Nodes that send us a Leave Request
            // We need to make sure we do not learn about these Nodes again
            private Dictionary<NodeId, DateTime> m_LeavingNodes = new Dictionary<NodeId, DateTime>();
            
            List<NodeId> m_UpdateReceivedFromUnattachedNode = new List<NodeId>();
            List<FTableEntry> m_FingerTable = new List<FTableEntry>();

            public Dictionary<NodeId, DateTime> LeavingNodes
            {
                get { return m_LeavingNodes; }
                //set { m_LeavingNodes = value; }
            }

            public Dictionary<string, RTableEntry> RtTable {
                get { return m_RtTable; }
            }

            public List<FTableEntry> FingerTable {
                get { return m_FingerTable; }
            }

            public void AddNode(Node node) {
                try {
                    String nodestr = node.Id.ToString();
                    lock (m_RtTable) {
                        if (!m_RtTable.ContainsKey(nodestr) &&
                          node.Id != m_local_node.Id) {
                            /* 
                             * important! we currently assume, that this function is called
                             * in context of attach ans, with exchanged ice 
                             * candidates in any other case attached should not be set to
                             * true. The flag attached signals that this node 
                             * can be part of the official successor/predecessor list
                             */
                            m_RtTable.Add(nodestr, new RTableEntry()
                            {
                                icecandidates = node.IceCandidates,
                                dtLastSuccessfullPing = DateTime.MinValue,
                                nodestate = NodeState.unknown,
                                node = node
                            });

                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                              String.Format("AddNode: {0}", node));
                        }
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      "Topology.AddNode: " + ex.Message);
                }

                /*Is a finger?*/
                if (isFinger(node.Id))
                    AddFinger(node, NodeState.attached);
            }
            internal void SetNodeState(NodeId nodeId, NodeState nodestate) {
                try {
                    RTableEntry rtable;

                    lock (m_RtTable) {
                        if (nodeId != null && m_RtTable.TryGetValue(nodeId.ToString(), out rtable)) {
                            NodeState oldstate = rtable.nodestate;

                            switch (rtable.nodestate) {
                                case NodeState.unknown:
                                    rtable.nodestate = nodestate;
                                    break;
                                case NodeState.attaching:
                                    rtable.nodestate = nodestate;
                                    break;
                                case NodeState.attached:
                                    if (nodestate == NodeState.attaching) {
                                        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SetNodeState: invalid transition from attached to attaching");
                                        //ignore, should only occur on AppAttach
                                    }
                                    else {
                                        if (nodestate == NodeState.attached & m_UpdateReceivedFromUnattachedNode.Contains(nodeId)) {
                                            m_UpdateReceivedFromUnattachedNode.Remove(nodeId);
                                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "SetNodeState: attached -> updates_received on saved state");
                                            rtable.nodestate = NodeState.updates_received;

                                        }
                                        else
                                            rtable.nodestate = nodestate;
                                    }
                                    break;
                                case NodeState.updates_received:
                                    //ignore attach messages here
                                    if (nodestate != NodeState.attached && nodestate != NodeState.attaching)
                                        rtable.nodestate = nodestate;
                                    break;
                            }

                            if (m_ReloadConfig.State == ReloadConfig.RELOAD_State.Joined
                                && (nodestate == NodeState.attached || rtable.nodestate == NodeState.updates_received)
                                && (oldstate == NodeState.attaching || oldstate == NodeState.unknown)) {
                                if (m_predecessors.Contains(nodeId) || m_successors.Contains(nodeId)) {
                                    /* A not approved node stored in successor and predecessor list became valid (attached).
                                     * This is a trigger to send an update to all (valid) neighbors
                                     */
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("AddNode: New approved neighbor, send updates to all"));
                                    SendUpdateToAllNeighbors();
                                }
                            }
                        }
                        else {
                            if (nodestate == NodeState.updates_received) {
                                /* bad situation, we received an update but probably no attach answ so far
                                 * save this info for later use */
                                m_UpdateReceivedFromUnattachedNode.Add(nodeId);
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SetNodeState: " + ex.Message);
                }
            }

            internal void SetPinging(NodeId nodeId, bool ping, bool success) {
                try {
                    RTableEntry rtable_entry;

                    lock (m_RtTable) {
                        if (nodeId != null && m_RtTable.TryGetValue(nodeId.ToString(), out rtable_entry)) {
                            rtable_entry.pinging = ping;
                            if (success)
                                rtable_entry.dtLastSuccessfullPing = DateTime.Now;
                        }
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SetPinging: " + ex.Message);
                }
            }

            internal void SetWaitForJoinAnsw(NodeId nodeId, Boolean fTrue) {
                try {
                    RTableEntry rtable_entry;

                    lock (m_RtTable) {
                        if (nodeId != null && m_RtTable.TryGetValue(nodeId.ToString(), out rtable_entry))
                            rtable_entry.wait_for_join_answ = fTrue;
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SetWaitForJoinAnsw: " + ex.Message);
                }
            }

            /// <summary>
            /// Returns the ice candidates stored in a dicationary along with a nodeid
            /// </summary>
            /// <param name="nodeid">The Node Id</param>
            /// <returns>List<IceCandidate></returns>
            /// <summary>
            public List<IceCandidate> GetCandidates(NodeId nodeid) {
                RTableEntry rtable;

                if (nodeid != null && m_RtTable.TryGetValue(nodeid.ToString(), out rtable))
                    return rtable.icecandidates;
                return null;
            }

            /// <summary>
            /// We need the information if a node (nodeid) has already been attached to
            /// and is valid. This is stored in the routing table
            /// </summary>
            /// <param name="nodeid">The Node Id</param>
            /// <returns>bool</returns>
            public NodeState GetNodeState(NodeId nodeid) {
                RTableEntry rtable;

                if (nodeid != null && m_RtTable.TryGetValue(nodeid.ToString(), out rtable)) {
                    return rtable.nodestate;
                }
                return NodeState.unknown;
            }

            public bool GetPing(NodeId nodeid) {
                RTableEntry rtable;

                if (nodeid != null && m_RtTable.TryGetValue(nodeid.ToString(), out rtable)) {
                    return rtable.pinging;
                }
                return false;
            }

            public bool IsWaitForJoinAnsw(NodeId nodeid) {
                RTableEntry rtable;

                if (nodeid != null && m_RtTable.TryGetValue(nodeid.ToString(), out rtable)) {
                    return rtable.wait_for_join_answ;
                }
                return false;
            }

            public bool IsAttached(NodeId nodeid) {
                NodeState nodestate = GetNodeState(nodeid);
                return nodestate == NodeState.attached || nodestate == NodeState.updates_received;
            }

            public bool GotUpdatesFrom(NodeId nodeid) {
                NodeState nodestate = GetNodeState(nodeid);
                return nodestate == NodeState.updates_received;
            }

            public char GetStatusShortLetter(NodeId nodeid) {
                NodeState nodestate = GetNodeState(nodeid);
                switch (nodestate) {
                    case NodeState.updates_received:
                        return 'U';
                    case NodeState.attached:
                        return 'A';
                    case NodeState.attaching:
                        return 'I';
                    case NodeState.unknown:
                        return '?';
                }
                return ' ';
            }

            public int GetSuccessorCount(bool only_approved) {
                int iCount = 0;
                for (int i = 0; i < m_successors.Count; ++i) {
                    if (m_successors[i] != null) {
                        if (!only_approved || IsAttached(m_successors[i]))
                            ++iCount;
                    }
                    else
                        break;
                }
                return iCount;
            }

            public int GetPredecessorCount(bool only_approved) {
                int iCount = 0;
                for (int i = 0; i < m_predecessors.Count; ++i) {
                    if (m_predecessors[i] != null) {
                        if (!only_approved || IsAttached(m_predecessors[i]))
                            ++iCount;
                    }
                    else
                        break;
                }
                return iCount;
            }

            private List<NodeId> m_successors;

            public List<NodeId> Successors {
                get { return m_successors; }
            }

            private List<NodeId> m_predecessors;

            public List<NodeId> Predecessors {
                get { return m_predecessors; }
            }

            private List<NodeId> m_fingerSuccessors;

            public List<NodeId> FingerSuccessors {
                get { return m_fingerSuccessors; }
            }

            private Node m_local_node;

            public Node GetNode(NodeId node_id) {
                RTableEntry rte;
                if (node_id == null)
                    return null;
                m_RtTable.TryGetValue(node_id.ToString(), out rte);
                if (rte != null)
                    return rte.node;
                else
                    return new Node(node_id, GetCandidates(node_id));
            }

            public NodeId GetApprovedSuccessor() {
                for (int i = 0; i < m_successors.Count; i++)
                    if (GotUpdatesFrom(m_successors[i]))
                        return m_successors[i];
                return m_local_node.Id;
            }

            public NodeId GetApprovedPredecessor() {
                for (int i = 0; i < m_predecessors.Count; i++)
                    if (GotUpdatesFrom(m_predecessors[i]))
                        return m_predecessors[i];
                return m_local_node.Id;
            }

            public Node GetSuccessor(int index) {
                if (index >= 0 && index < m_successors.Count)
                    return GetNode(m_successors[index]);
                return null;
            }

            public NodeId GetSuccessorId(int index) {
                if (index >= 0 && index < m_successors.Count)
                    return m_successors[index];
                return null;
            }

            public Node GetPredecessor(int index) {
                if (index >= 0 && index < m_predecessors.Count)
                    return GetNode(m_predecessors[index]);
                return null;
            }

            public NodeId GetPredecessorId(int index) {
                if (index >= 0 && index < m_predecessors.Count)
                    return m_predecessors[index];
                return null;
            }

            private int currFingerNo = 0;

            public int CurrFingerNo {
                get { return this.currFingerNo; }
            }

            /// <summary>
            /// This method should only be executed after enrichment
            /// of the connection table while joining. It transfers all 
            /// connection obtain yet to the routing table.
            /// 
            /// see base -18 p.106
            /// 4.  JP MUST enter all the peers it has contacted into its routing
            ///     table.
            /// </summary>
            /// <returns></returns>
            public void Conn2Route() {
                lock ("conn to route") {
                    foreach (RTableEntry rte in m_RtTable.Values)
                        if (rte.nodestate == NodeState.attached) {
                            rte.nodestate = NodeState.updates_received;
                        }
                    foreach (FTableEntry fte in m_FingerTable)
                        if (fte.nodestate == NodeState.attached) {
                            fte.nodestate = NodeState.updates_received;
                        }
                }
            }

            public RoutingTable(Node local_node, Machine machine) {
                m_ReloadConfig = machine.ReloadConfig;
                m_flm = machine.Interface_flm;
                m_DispatcherQueue = machine.ReloadConfig.DispatcherQueue;
                m_transport = machine.Transport;
                m_local_node = local_node;
                m_successors = new List<NodeId>();
                m_predecessors = new List<NodeId>();
                m_fingerSuccessors = new List<NodeId>();

                for (int i = 0; i < 128; i++)
                    m_FingerTable.Add(new FTableEntry()
                    {
                        Finger = new ResourceId(local_node.Id + ReloadGlobals.BigIntPow2Array[i]),
                        dtLastSuccessfullFinger = DateTime.MinValue,
                        nodestate = NodeState.unknown
                    });

                /* 
                   A peer MUST NOT enter
                   itself in its successor or predecessor table and instead should leave
                   the entries empty.
                 */
            }

            public Node FindNextHopTo(NodeId TargetId, bool fIncluding, bool fExludeMe) {

                /* Nodes of the connection table may be not part of the ring. So never
                 * use them as via nodes
                 */
                foreach (ReloadConnectionTableInfoElement rce in m_flm.ConnectionTable)
                    if (rce.NodeID != null && rce.NodeID == TargetId)
                        return GetNode(rce.NodeID);

                /* Already in routing table? Route to destination directly then */
                if (GotUpdatesFrom(TargetId))
                    return GetNode(TargetId);

                /* As Client always contact admitting peer and do not forward anything
                 * as long as not having joined (bootstraps are an exception)
                 */
                if (m_ReloadConfig.IamClient)
                    return m_ReloadConfig.AdmittingPeer;

                /* Now use any information available to find the hop nearest to 
                 * destination where we have ice candidates of or have an existing 
                 * connection to
                 */

                if (m_successors.Count == 0)
                    return m_local_node;
                if (TargetId.ElementOfInterval(GetPredecessor(0).Id, m_local_node.Id, true))
                    return m_local_node;
                foreach (NodeId succ in m_successors) {
                    if (TargetId.ElementOfInterval(m_local_node.Id, succ, false) ||
                        TargetId == succ)
                        return GetNode(succ);
                }

                Node closestNode = GetClosestPrecedingNode(TargetId);

                return closestNode;

                //return FindNextHopTo(closestNode.Id, false , false);

                // watch out! NextHopId might be null
                //return GetNode(NextHopId);
            }

            private Node GetClosestPrecedingNode(NodeId key) {
                /*  List of all remote peers*/
                List<NodeId> mexthop_list = new List<NodeId>();
                /* insert all successors into list */
                mexthop_list.AddRange(m_successors);
                /* predecessor is appropriate only if it precedes the given id */
                if (m_predecessors.Count > 0)
                    foreach (NodeId pre in m_predecessors)
                        if (key.ElementOfInterval(pre, m_local_node.Id, false))
                            mexthop_list.AddRange(m_predecessors);
                /* determine closest preceding reference of finger table */
                Node closetPrecedingFinger = GetClosestPrecedingFinger(key);

                if (closetPrecedingFinger != null)
                    mexthop_list.Add(closetPrecedingFinger.Id);

                Node closestNode = null;
                mexthop_list.Add(key);

                mexthop_list = removeDuplicates(mexthop_list);
                int sizeOfList = mexthop_list.Count;
                if (sizeOfList > 1)
                    mexthop_list.Sort();

                /*
                 * The list item with one index lower than that of the key must be the
                 * id of the closest predecessor or the key.
                 */
                int keyIndex = mexthop_list.IndexOf(key);
                /*
               * As all ids are located on a ring if the key is the first item in the
               * list we have to select the last item as predecessor with help of this
               * calculation.
               */
                int index = (sizeOfList + (keyIndex - 1)) % sizeOfList;

                NodeId idOfClosestNode = mexthop_list[index];

                closestNode = GetNode(idOfClosestNode);

                return closestNode;
            }

            private Node GetClosestPrecedingFinger(NodeId key) {
                List<NodeId> successorIds = new List<NodeId>();
                List<FTableEntry> successors = new List<FTableEntry>();

                foreach (FTableEntry ft in FingerTable)
                    if (!successorIds.Contains(ft.Successor) &&
                        ft.nodestate == NodeState.updates_received) {
                        successorIds.Add(ft.Successor);
                        successors.Add(ft);
                    }

                foreach (FTableEntry finger in successors) {
                    if (finger.Successor != null &&
                        finger.Successor.ElementOfInterval(m_local_node.Id, key, false)// &&
                        )//GotUpdatesFrom(finger.Successor))
                        return GetNode(finger.Successor);
                }

                return null;
            }

            public void Reset() {
                m_successors.Clear();
                m_predecessors.Clear();

                foreach (FTableEntry fte in FingerTable) {
                    fte.Successor = null;
                    fte.valid = false;
                }
            }

            /// <summary>
            /// JP also needs to populate its finger table (for the Chord based DHT).
            /// It issues an Attach to a variety of locations around the overlay.
            /// see RELOAD base -17 p.129
            /// </summary>
            /// <returns></returns>
            public List<FTableEntry> AttachFingers() {

                var fingerCount = ReloadGlobals.FINGER_TABLE_ENTRIES;

                if (!((fingerCount & (fingerCount - 1)) == 0)) // fingerCount = 2^n ?
                    fingerCount = 16; // fallback to 16 entries

                List<FTableEntry> fingers = new List<FTableEntry>();

                int offset = m_FingerTable.Count / fingerCount;

                for (int i = 0; i < m_FingerTable.Count; i += offset) {
                    var res = m_FingerTable[i].Finger;
                    Destination dest = new Destination(m_FingerTable[i].Finger);
                    fingers.Add(m_FingerTable[i]);
                }
                return fingers;

            } // end AttachFingers

            /// <summary>
            /// Returns true if attaching Node should be added to finger table.
            /// </summary>
            /// <param name="testFinger">The attaching node</param>
            /// <param name="finger">The first Finger Table entry machting the attaching node.</param>
            /// <returns>Boolean</returns>
            public bool isFinger(NodeId testFinger, out FTableEntry finger) {
                foreach (FTableEntry ftEntry in FingerTable) {
                    if (ftEntry.nodestate == NodeState.unknown) {
                        if (ftEntry.Finger.ElementOfInterval(m_local_node.Id, testFinger, true)) {
                            finger = ftEntry;
                            return true;
                        }
                    }
                    else { // is closer?
                        if (testFinger.ElementOfInterval(ftEntry.Finger, ftEntry.Successor, false)) {
                            finger = ftEntry;
                            return true;
                        }
                    }
                }
                finger = null;
                return false;
            }

            /// <summary>
            /// Returns true, if the tested Finger is needed for fingertable
            /// </summary>
            /// <param name="testFinger"></param>
            /// <returns></returns>
            public bool isFinger(NodeId testFinger) {
                FTableEntry finger = null;
                return isFinger(testFinger, out finger);
            }

            public bool isNewNeighbour(NodeId attacher) {
                if (m_predecessors.Count == 0 && m_successors.Count == 0)
                    return true;

                List<NodeId> updateList = new List<NodeId>(m_successors);
                updateList.AddRange(m_predecessors);
                updateList.Add(attacher);
                updateList = removeDuplicates(updateList);

                List<NodeId> newPres = NeighborsFromTotal(updateList, false);
                List<NodeId> newSuccs = NeighborsFromTotal(updateList, true);


                if (IsChangedList(m_predecessors, newPres) ||
                    IsChangedList(m_successors, newSuccs)) {
                    return true;
                }

                return false;
            }

            public void SetFingerState(NodeId finger, NodeState state) {
                foreach (FTableEntry fte in m_FingerTable)
                    if (fte.Successor == finger)
                        fte.nodestate = state;
            }

            /// <summary>
            /// Adds a new finger and sets its state.
            /// Note, only fingers for which we received a update
            /// will be used for routing.
            /// </summary>
            /// <param name="testFinger">The remote finger</param>
            /// <param name="state">Normally, attached because update has no Node
            /// </param>
            public void AddFinger(Node testFinger, NodeState state) {
                if (m_ReloadConfig.IamClient)
                    return;

                foreach (FTableEntry ftEntry in FingerTable) {
                    if (!m_fingerSuccessors.Contains(ftEntry.Successor))
                        m_fingerSuccessors.Add(ftEntry.Successor);
                    if (ftEntry.nodestate == NodeState.unknown) {
                        if (ftEntry.Finger.ElementOfInterval(m_local_node.Id, testFinger.Id, true)) {
                            ftEntry.dtLastSuccessfullFinger = DateTime.Now;
                            ftEntry.nodestate = state;
                            ftEntry.pinging = false;
                            ftEntry.Successor = testFinger.Id;
                            ftEntry.valid = true;
                        }
                    }
                    /* Node if confirmed as routable */
                    else if (ftEntry.nodestate == NodeState.attached &&
                      state == NodeState.updates_received) {
                        ftEntry.nodestate = state;
                    }
                    else {   // is closer?
                        if (testFinger.Id.ElementOfInterval(ftEntry.Finger, ftEntry.Successor, false)) {
                            ftEntry.dtLastSuccessfullFinger = DateTime.Now;
                            ftEntry.nodestate = state;
                            ftEntry.pinging = false;
                            ftEntry.Successor = testFinger.Id;
                            ftEntry.valid = true;
                        }
                    }
                }
                currFingerNo = m_fingerSuccessors.Count;
            }

            /// <summary>
            /// If any peer sends an update we merge this information with our local tables
            /// </summary>
            /// <param name="Originator">The originator of the update message</param>
            /// <param name="successors">List of successors provided</param>
            /// <param name="predecessors">List of predecessors provided</param>
            /// <param name="transport">The transport layer.</param>
            /// <param name="transport">The forwarding layer.</param>
            /// <returns>IEnumerator<ITask></returns>
            internal IEnumerator<ITask> Merge(NodeId originator, UpdateReqAns req_answ, Boolean ForceSendUpdate) {
                /*
                When a peer, N, receives an Update request, it examines the Node-IDs
                   in the UpdateReq and at its neighbor table and decides if this
                 * UpdateReq would change its neighbor table.*/
                lock ("merge") {
                    try {

                        if (!IsAttached(originator))
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("A none attached node sent me an update {0}:", originator));

                        List<NodeId> total_update_list = new List<NodeId>();

                        var validSuccessors = new List<NodeId>();
                        var validPredecessors = new List<NodeId>();

                        if (LeavingNodes.Count > 0)
                        {
                            if (!LeavingNodes.ContainsKey(originator))
                                total_update_list.Add(originator);

                            List<NodeId> keys = LeavingNodes.Keys.ToList();

                            foreach (NodeId node in req_answ.Successors)
                            {
                                if (!keys.Contains(node))
                                    validSuccessors.Add(node);
                            }

                            total_update_list.AddRange(validSuccessors);

                            foreach (NodeId node in req_answ.Predecessors)
                            {
                                if (!keys.Contains(node))
                                    validPredecessors.Add(node);
                            }

                            total_update_list.AddRange(validPredecessors);
                        }
                        else
                        {
                            total_update_list.Add(originator);
                            total_update_list.AddRange(req_answ.Successors);
                            total_update_list.AddRange(req_answ.Predecessors);
                        }
                            

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("<== Successors from {0}:", originator));
                        for (int i = req_answ.Successors.Count - 1; i >= 0; i--)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    S{0}: {1}", i, req_answ.Successors[i]));
                        }

                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("<== Predecessors from {0}:", originator));
                        for (int i = 0; i < req_answ.Predecessors.Count; i++)
                        {
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    P{0}: {1}", i, req_answ.Predecessors[i]));
                        }

                        total_update_list = removeDuplicates(total_update_list);

                        if (m_ReloadConfig.IamClient) {
                            if (m_ReloadConfig.AdmittingPeer != null) {
                                NodeId nodeid = m_ReloadConfig.AdmittingPeer.Id;

                                foreach (NodeId id in total_update_list) {
                                    if (id.ElementOfInterval(m_local_node.Id, nodeid, false))
                                        nodeid = id;
                                }

                                //found a new admitting (responsible) peer
                                if (nodeid != m_local_node.Id && nodeid != m_ReloadConfig.AdmittingPeer.Id) {
                                    NodeState nodestate = GetNodeState(nodeid);
                                    if (nodestate == NodeState.unknown) {
                                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
                                          String.Format("Found a new admitting peer: {0} via {1}",
                                          nodeid, originator));
                                        Arbiter.Activate(m_DispatcherQueue,
                                          new IterativeTask<Destination, NodeId, AttachOption>(
                                          new Destination(nodeid), originator,
                                          AttachOption.forceupdate, m_transport.AttachProcedure));
                                    }
                                }
                            }
                            yield break;
                        }

                        foreach (NodeId id in total_update_list) {
                            if (id != originator) {
                                NodeId value;
                                if (m_LearnedFromTable.TryGetValue(id.ToString(), out value)) {
                                    if (IsAttached(originator) && value != originator) {
                                        m_LearnedFromTable.Remove(id.ToString());
                                        m_LearnedFromTable.Add(id.ToString(), originator);
                                        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    Upd: Learned {0} from {1}", id, originator));
                                    }
                                }
                                else {
                                    m_LearnedFromTable.Add(id.ToString(), originator);
                                    //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("    New: Learned {0} from {1}", id, originator));
                                }
                            }
                        }

                        total_update_list.AddRange(m_successors);
                        total_update_list.AddRange(m_predecessors);

                        total_update_list = removeDuplicates(total_update_list);

                        List<NodeId> m_new_predecessors = NeighborsFromTotal(total_update_list, false);
                        List<NodeId> m_new_successors = NeighborsFromTotal(total_update_list, true);

                        // REPLICATEST
                        foreach (NodeId node in LeavingNodes.Keys)
                        {
                            if (m_new_predecessors.Contains(node))
                                m_new_predecessors.Remove(node);

                            if (m_new_successors.Contains(node))
                                m_new_successors.Remove(node);
                        }

                        Boolean fAnyNewNeighbor = false;

                        /*                      if(    IsChangedList(GetApproved(m_predecessors), GetApproved(m_new_predecessors))
                                                    || IsChangedList(GetApproved(m_successors), GetApproved(m_new_successors)))
                                                {
                                                    fNewUpdateRequired = true;
                                                }
                        */
                        if (IsChangedList(m_predecessors, m_new_predecessors)
                            || IsChangedList(m_successors, m_new_successors)) {
                            fAnyNewNeighbor = true;
                        }

                        if (IsChangedList(m_predecessors, m_new_predecessors)) {
                            //got new predecessor. Handover Keys
                            Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<bool>(false, m_transport.HandoverKeys));
                        }

                        if (IsChangedList(m_successors, m_new_successors))
                        {
                            List<NodeId> nodesWithReplicas = new List<NodeId>();
                            if(m_successors.Count > 0)
                                nodesWithReplicas.Add(m_successors[0]);
                            if (m_successors.Count > 1)
                                nodesWithReplicas.Add(m_successors[1]);

                            if(m_new_successors.Count > 0)
                                if(!nodesWithReplicas.Contains(m_new_successors[0]))
                                    Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_new_successors[0], m_transport.StoreReplicas));

                            if (m_new_successors.Count > 1)
                                if(!nodesWithReplicas.Contains(m_new_successors[1]))
                                    Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_new_successors[1], m_transport.StoreReplicas));
                        }

                        m_predecessors = m_new_predecessors;
                        m_successors = m_new_successors;

                        // update all neighbors on changes in the local neighbor table
                        if (fAnyNewNeighbor || ForceSendUpdate) {
                            PrintNeigborState();
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("Merge: New approved neighbors, send updates to all"));
                            SendUpdateToAllNeighbors();
                        }

                    }
                    catch (Exception ex) {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Merge: " + ex.Message);
                        ReloadGlobals.PrintException(m_ReloadConfig, ex);
                        //System.Diagnostics.Debugger.Break();
                    }
                }

                // 6.  AP MUST do a series of Store requests to JP to store the data
                // that   will be responsible for. RELOAD base -13 p.105
                // m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_RELOAD, "Handover Keys");
                // Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Boolean>(false, m_transport.HandoverKeys));


                yield break;
            }

            public void PrintNeigborState() {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Successors:");

                for (int i = m_successors.Count - 1; i >= 0; i--) {
                    NodeId id = m_successors[i];
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("    S{0}: {1} {2}", i, GetNode(id), GetStatusShortLetter(id)));
                }

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, "Predecessors:");
                for (int i = 0; i < m_predecessors.Count; i++) {
                    NodeId id = m_predecessors[i];
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("    P{0}: {1} {2}", i, GetNode(id), GetStatusShortLetter(id)));
                }
            }

            private bool IsChangedList(List<NodeId> m_old_list, List<NodeId> m_new_list) {
                if (m_old_list.Count != m_new_list.Count)
                    return true;

                for (int i = 0; i < m_old_list.Count; i++)
                    if (m_old_list[i] != m_new_list[i])
                        return true;

                return false;
            }

            /* cancelled dev of storage of list because it looks like of no use
                        private bool removeLost(List<NodeId> nodelist, UpdateReqAns req_answ, UpdateReqAns stored_req_answ)
                        {
                            foreach (NodeId nodeid in nodelist)
                            {
                                if     (stored_req_answ.Successors.Contains(nodeid) || stored_req_answ.Predecessors.Contains(nodeid))
                                { 
                    
                    
                                }
                            }
                        }
             */

            public List<NodeId> NeighborsFromTotal(List<NodeId> total_update_list, bool fSuccessors) {
                List<NodeId> new_list = new List<NodeId>();
                //              int x = 0;
                foreach (NodeId nodeid in total_update_list) {
                    //                   m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("==== {0} round=====",++x));
                    //exclude own id
                    if (nodeid == m_local_node.Id)
                        continue;

                    if (new_list.Count == 0) {
                        new_list.Add(nodeid);
                        //                      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("{0} new_list adding {1}",fSuccessors?"S":"P", nodeid.ToString()));
                        continue;
                    }

                    bool fInserted = false;

                    for (int i = 0; i < new_list.Count; i++) {
                        if (fSuccessors) {
                            if (nodeid.ElementOfInterval(i == 0 ? m_local_node.Id : new_list[i - 1], new_list[i], false)) {
                                //                             m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("{0} new_list insert {1} {2}", fSuccessors ? "S" : "P", i, nodeid.ToString()));
                                new_list.Insert(i, nodeid);
                                fInserted = true;
                                break;
                            }
                        }
                        else
                            if (nodeid.ElementOfInterval(new_list[i], i == 0 ? m_local_node.Id : new_list[i - 1], false)) {
                                //                              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("{0} new_list insert {1} {2}", fSuccessors ? "S" : "P", i, nodeid.ToString()));
                                new_list.Insert(i, nodeid);
                                fInserted = true;
                                break;
                            }
                    }
                    if (!fInserted) {
                        //                      m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("{0} new_list adding2 {1}", fSuccessors ? "S" : "P", nodeid.ToString()));
                        new_list.Add(nodeid);
                    }
                }
                //shrink to maximum cache size
                if (new_list.Count > ReloadGlobals.SUCCESSOR_CACHE_SIZE)
                    new_list.RemoveRange(ReloadGlobals.SUCCESSOR_CACHE_SIZE, new_list.Count - ReloadGlobals.SUCCESSOR_CACHE_SIZE);
                return new_list;
            }

            /// <summary>
            /// Attach to all members of the successor and predecessor table
            /// </summary>
            /// <param name="transport">The transport layer.</param>
            /// <returns></returns>
            public void AttachToAllNeighbors() {
                try {
                    List<NodeId> total_attach_list = new List<NodeId>();

                    total_attach_list.AddRange(m_successors);
                    total_attach_list.AddRange(m_predecessors);
                    total_attach_list = removeDuplicates(total_attach_list);

                    foreach (NodeId nodeid in total_attach_list) {
                        if (nodeid != null) {
                            if (GetNodeState(nodeid) == NodeState.unknown) {
                                NodeId learnedfromnode;

                                if (m_LearnedFromTable.TryGetValue(nodeid.ToString(), out learnedfromnode)) {
                                    if (IsAttached(learnedfromnode))
                                        /* Attach to all new neighbors via sourcerouting to get ice candidates */
                                        Arbiter.Activate(m_DispatcherQueue,
                                          new IterativeTask<Destination, NodeId, AttachOption>(
                                          new Destination(nodeid), learnedfromnode,
                                          AttachOption.forceupdate | AttachOption.sendping,
                                          m_transport.AttachProcedure));
                                }
                                else {
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Lost info about where I learned {0} from, maybe this none approved node sent me the update", nodeid));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AttachToAllNeighbors: " + ex.Message);
                    throw ex;
                }
            }

            /// <summary>
            /// Sends and update to all members of the successor and predecessor table
            /// </summary>
            /// <param name="transport">The transport layer.</param>
            /// <returns></returns>
            public void SendUpdateToAllNeighbors() {
                if (!IsAttachedToAllNeighbors()) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Not attached to all neighbors don't send updates");
                    AttachToAllNeighbors();
                    return;
                }

                //don't send updates as client
                if (m_ReloadConfig.State != ReloadConfig.RELOAD_State.Joined) {
                  if (!m_ReloadConfig.IsBootstrap)
                  {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                        "Not joined, don't send updates to all");
                  }
                  return;
                }
                else
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO,
                      String.Format("Send updates to all"));

                try {
                    List<NodeId> total_update_list = new List<NodeId>();

                    total_update_list.AddRange(m_successors);
                    total_update_list.AddRange(m_predecessors);
                    //total_update_list.AddRange(m_fingerSuccessors);

                    /*there might be attached clients, which need this infomation
                     * (possible Admitting Peer shifting)
                     */
                    foreach (ReloadConnectionTableInfoElement rce in m_transport.GetForwardingAndLinkManagementLayer().ConnectionTable)
                        if (rce.AssociatedSocket.Connected)
                            if (IsAttached(rce.NodeID))
                                total_update_list.Add(rce.NodeID);

                    total_update_list = removeDuplicates(total_update_list);

                    /* All overlay algorithms MUST specify maintenance procedures that send
                       Updates to clients and peers that have established connections to the
                       peer responsible for a particular ID when the responsibility for that
                       ID changes.  Because tracking this information is difficult, overlay
                       algorithms MAY simply specify that an Update is sent to all members
                       of the Connection Table whenever the range of IDs for which the peer
                       is responsible changes.
                     */

                    foreach (NodeId nodeid in total_update_list) {
                        if (nodeid != null) {
                            if (IsAttached(nodeid))
                                Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, Node>(
                                  GetNode(nodeid), null, m_transport.SendUpdate));
                        }
                    }
                }
                catch (Exception ex) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                      "SendUpdateToAllNeighbors: " + ex.Message);
                    throw ex;
                }
            }

            /// <summary>
            /// Sends updates to all Fingers. Skips Successors and Predecessors
            /// </summary>
            /* Obsolet
            public void SendUpdatesToAllFingers_old() {
              if (!IsAttachedToAllNeighbors()) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Not attached to all neighbors don't send updates");
                AttachToAllNeighbors();
                return;
              }

              //don't send updates as client
              if (m_ReloadConfig.State != ReloadConfig.RELOAD_State.Joined) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Not joined, don't send updates to all");
                return;
              }
              else
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO, String.Format("Send updates to Fingers"));

              try {
                List<NodeId> total_update_list = new List<NodeId>();
                total_update_list.AddRange(m_fingerSuccessors);

                // Remove pres and succs, because they will be updated through SendUpdateToAllNeighbours()
                for (int i = 0; i < total_update_list.Count; i++) {
                  for (int j = 0; j < m_successors.Count; j++)
                    if (total_update_list[i] == m_successors[j]) {
                      total_update_list.RemoveAt(i);
                      continue;
                    }
                  for (int j = 0; j < m_predecessors.Count; j++)
                    if (total_update_list[i] == m_predecessors[j]) {
                      total_update_list.RemoveAt(i);
                    }
                }
                total_update_list = removeDuplicates(total_update_list);

                foreach (NodeId nodeid in total_update_list) {
                  if (nodeid != null) {
                    if (IsAttached(nodeid))
                      Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, Node>(GetNode(nodeid), null, m_transport.SendUpdate));
                  }
                }

              }
              catch (Exception e) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SendUpdatesToAllFingers: " + e.Message);
              }
            }
            */

            /// <summary>
            /// Removes duplicates in any list of node id's
            /// </summary>
            /// <param name="inputList">A list containing node id's</param>
            /// <returns>Cleaned list</returns>
            public List<NodeId> removeDuplicates(List<NodeId> inputList) {
                List<NodeId> finalList = new List<NodeId>();
                foreach (NodeId currValue in inputList) {
                    if (!finalList.Contains(currValue)) {
                        finalList.Add(currValue);
                    }
                }
                return finalList;
            }

            public List<FTableEntry> rmDuplicateFingers(List<FTableEntry> fingers) {
                List<FTableEntry> finalList = new List<FTableEntry>();
                List<NodeId> successors = new List<NodeId>();
                foreach (FTableEntry fte in fingers) {
                    if (fte.Successor != null && !successors.Contains(fte.Successor)) {
                        successors.Add(fte.Successor);
                        finalList.Add(fte);
                    }
                }
                return finalList;
            }

            public List<NodeId> GetApproved(List<NodeId> inputList) {
                List<NodeId> approvedList = new List<NodeId>();
                foreach (NodeId currValue in inputList)
                    if (GotUpdatesFrom(currValue))
                        approvedList.Add(currValue);
                return approvedList;
            }

            public bool NodeWeNeed(NodeId nodeId) {
                if (m_ReloadConfig.AdmittingPeer != null && m_ReloadConfig.AdmittingPeer.Id != null)
                    if (nodeId == m_ReloadConfig.AdmittingPeer.Id)
                        return true;

                if (m_successors.Contains(nodeId))
                    return true;

                if (m_predecessors.Contains(nodeId))
                    return true;

                foreach (FTableEntry fte in FingerTable)
                    if (fte.Successor == nodeId)
                        return true;

                return false;
            }

            public bool Leave(NodeId nodeId) {
                /* 
                 * A node leaves the overlay, do we have to notify the neighbors?
                 * (Which is the case if this node was part of our lists)
                 */
                bool fUpdateNeeded = false;
                bool fWasAdmittingPeer = false;
                bool fEvaluateReplicas = false;

                SetNodeState(nodeId, NodeState.unknown);

                if (m_ReloadConfig.AdmittingPeer != null && m_ReloadConfig.AdmittingPeer.Id != null)
                    if (nodeId == m_ReloadConfig.AdmittingPeer.Id) {
                        m_ReloadConfig.AdmittingPeer = null;
                        fWasAdmittingPeer = true;
                    }

                if (m_successors.Contains(nodeId)) {
                    int index = m_successors.IndexOf(nodeId);

                    m_successors.Remove(nodeId);
                    // Leaving nodes will be stored for 5 minutes to make sure we do not learn about them again
                    AddLeavingNode(nodeId);
                    fUpdateNeeded = true;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Deleted {0} from Successors", nodeId));

                    // Has Successor 1 or 2 crashed? Do we need to send out a new store request for replicas?
                    if (index < 2 && m_successors.Count > 1)
                        Arbiter.Activate(m_ReloadConfig.DispatcherQueue, new IterativeTask<NodeId>(m_successors[1], m_transport.StoreReplicas));
                }

                if (m_predecessors.Contains(nodeId)) {
                    int index = m_predecessors.IndexOf(nodeId);
                    if (index == 0)
                        fEvaluateReplicas = true;

                    m_predecessors.Remove(nodeId);
                    AddLeavingNode(nodeId);
                    fUpdateNeeded = true;
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Deleted {0} from Predecessors", nodeId));

                    if (fEvaluateReplicas == true)
                        m_transport.EvaluateReplicas();
                }

                foreach (FTableEntry fte in FingerTable)
                    if (fte.Successor == nodeId) {
                        fte.Successor = null;
                        fte.valid = false;
                    }

                if (m_RtTable.ContainsKey(nodeId.ToString()))
                {
                    m_RtTable.Remove(nodeId.ToString());
                    AddLeavingNode(nodeId);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ALL, String.Format("Deleted {0} from Routing Table", nodeId));
                    fUpdateNeeded = true;
                }

                if (fUpdateNeeded && !m_ReloadConfig.IamClient) {
                    SendUpdateToAllNeighbors();
                }

                if (fWasAdmittingPeer) {
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Lost admitting peer {0}: starting prejoin procedure", nodeId));
                    Arbiter.Activate(m_DispatcherQueue, new IterativeTask<List<BootstrapServer>>(m_ReloadConfig.ThisMachine.BootstrapServer, m_ReloadConfig.ThisMachine.Transport.PreJoinProdecure));
                }
                return fWasAdmittingPeer;
            }

            internal bool IsAttachedToAllNeighbors() {
                foreach (NodeId id in m_successors)
                    if (!IsAttached(id))
                        return false;

                foreach (NodeId id in m_predecessors)
                    if (!IsAttached(id))
                        return false;

                return true;
            }

            public void AddLeavingNode(NodeId OriginatorId)
            {
                if(!m_LeavingNodes.ContainsKey(OriginatorId))
                    m_LeavingNodes.Add(OriginatorId, DateTime.Now);
            }
        }

        private SortedList<string, byte[]> m_data_store = new SortedList<string, byte[]>();
        private MessageTransport m_transport = null;
        private ReloadConfig m_ReloadConfig = null;
        private RoutingTable m_routing_table = null;
        public RoutingTable routing_table {
            get { return m_routing_table; }
        }

        private Node m_localnode = null;
        public Node LocalNode {
            get { return m_localnode; }
        }

        public TopologyPlugin(Machine machine) {
            m_id = machine.ReloadConfig.LocalNodeID;
            m_transport = machine.Transport;
            m_DispatcherQueue = machine.ReloadConfig.DispatcherQueue;
            m_ReloadConfig = machine.ReloadConfig;
            storage = new StorageModul(m_ReloadConfig);
        }

        public bool Init(Machine machine) {
#if false
            Console.Title = String.Format("{3}Node {0}:{1}, ID {2}", ReloadGlobals.HostName, ReloadGlobals.ListenPort, ReloadGlobals.LocalNodeID, ReloadGlobals.SimpleNodeId ? " DEMO " : "");
#endif
            IPAddress Address = null;

            if (ReloadGlobals.HostName != "") {
                Address = ReloadGlobals.IPAddressFromHost(m_ReloadConfig,
                  ReloadGlobals.HostName);
            }
            //TKTEST IETF
           // Address =  new IPAddress(new byte[] { 192, 168, 2, 147 });
            if (Address == null) {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
                  "Couldn't figure out my IP Address");
                return false;
            }

            /*use the attach request functionaliy to store 
             * ip-address and port to ice candidates in local node */
            AttachReqAns attreq = new AttachReqAns();
            m_localnode = new Node(m_id, attreq.IPAddressToIceCandidate(
              Address, m_ReloadConfig.ListenPort));

            
            m_localnode.NoIceCandidates = m_localnode.IceCandidates;  // markus: backup of NOICE candidates


            m_routing_table = new RoutingTable(m_localnode, machine);
            m_ReloadConfig.LocalNode = m_localnode;

            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TOPO,
              String.Format("Local node is {0}", m_localnode));

            return true;
        }

        internal void Leave() {
            routing_table.Reset();
        }

        internal void InboundLeave(NodeId nodeId) {
            routing_table.Leave(nodeId);
        }

        /// <summary>
        /// This method maintains data storage of incomming store requests.
        /// </summary>
        /// <param name="resource_id"></param>
        /// <param name="kind_data"></param>        
        public void Store(ResourceId resource_id, StoreKindData kind_data) {

            storage.Store(resource_id, kind_data);
        }

        public bool Fetch(ResourceId res_id, StoredDataSpecifier specifier, out FetchKindResponse kindResponse) {

            FetchKindResponse result = null;
            if (storage.Fetch(res_id, specifier, out result)) {
                kindResponse = result;
                return true;
            }
            kindResponse = new FetchKindResponse();
            return false;

        }
    }
}