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
using TSystems.RELOAD.ForwardAndLinkManagement;
using System.Net;
using System.Collections;
using Microsoft.Ccr.Core;
using TSystems.RELOAD.Topology;
using TSystems.RELOAD.Utils;

namespace TSystems.RELOAD.Transport {

    public class ReloadMessageFilter {
        public UInt64? transactionID;
        public ReloadMessageFilter(UInt64 id)
        {
            transactionID = id;
        }
    }
    public class ReloadDialog {
        //Structure: Dictionary<reloadMsg.TransactionID, SortedDictionary<fragment.Offset, fragment>>
      private Dictionary<UInt64, SortedDictionary<UInt32, MessageFragment>> fragmentedMessageBuffer = new Dictionary<ulong, SortedDictionary<uint, MessageFragment>>();
        
        private ReloadMessage m_ReceivedMessage = null;
        public ReloadMessage ReceivedMessage
        {
            get { return m_ReceivedMessage; }
        }
        private NodeId m_SourceNodeID = null;
        public NodeId SourceNodeID {
            get { return m_SourceNodeID; }
        }
        private Port<bool> m_fDone;
        public Port<bool> Done {
            get { return m_fDone; }
        }

        private Port<bool> m_portWaitForRx = new Port<bool>();

        private bool m_fError = false;
        public bool Error {
            get { return m_fError; }
            set { m_fError = value; }
        }

        private Node m_NextHopNode;
        private Queue m_Queue = new Queue();
        private DateTime m_TimeStart;
        private IForwardLinkManagement m_Transport;
        private DispatcherQueue m_DispatcherQueue;
        private ReloadConfig m_ReloadConfig;

        public ReloadDialog(ReloadConfig reloadConfig, IForwardLinkManagement transport, Node nexthopnode) {
            this.m_NextHopNode = nexthopnode;
            this.m_Transport = transport;
            this.m_fDone = new Port<bool>();
            this.m_DispatcherQueue = reloadConfig.DispatcherQueue;
            this.m_ReloadConfig = reloadConfig;
            m_Transport.ReloadFLMEventHandler += new ReloadFLMEvent(OVL_ReloadForwardLinkManagementEventHandler);

            if(nexthopnode.IceCandidates == null)
                throw new System.Exception(String.Format("ReloadDialog: no ice candidates for {0} ", nexthopnode.Id));
        }


        ~ReloadDialog() {
        }

        private ReloadFLMEventArgs OVL_ReloadForwardLinkManagementEventHandler(object sender, ReloadFLMEventArgs args) {
            switch (args.Eventtype) {
                case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK:
/*                  {
                        ReloadMessage reloadMsg = new ReloadMessage().FromBytes(args.Message);

                        if (reloadMsg != null)
                            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TEST, String.Format("Dialog in: {0} from {1} TransID={2:x16}", reloadMsg.reload_message_body.RELOAD_MsgCode.ToString().PadRight(16, ' '), reloadMsg.OriginatorID, reloadMsg.TransactionID));
                    }
*/
                    m_Queue.Enqueue(args);
                    if (m_portWaitForRx != null)
                        m_portWaitForRx.Post(true);
                    else
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("portWaitForRx = null!"));
                    break;
                case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_FRAME_SEND_BUFFER:
                    break;
                case ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_STATUS_CONNECT_FAILED:
                    m_fError = true;
                    if (m_portWaitForRx != null)
                        m_portWaitForRx.Post(true);
                    break;
            }
            return args;
        }

        private IEnumerator<ITask> Send(ReloadMessage reloadMessage) {
            //m_Transport.Send(this.m_NextHopNode, reloadMessage);
            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, ReloadMessage>(this.m_NextHopNode, reloadMessage, m_Transport.Send));
            m_TimeStart = DateTime.Now;
            yield break;
        }


        Port<DateTime> timeouted = new Port<DateTime>();


        /// <summary>
        /// TASK: Waits for the reception for max rx_timeout milliseconds. Filters if required.
        /// </summary>
        /// <param name="rx_filter">The rx_filter.</param>
        /// <param name="rx_timeout">The rx_timeout.</param>
        /// <returns></returns>
        private IEnumerator<ITask> Receive(ReloadMessageFilter rx_filter, int rx_timeout) {

            m_DispatcherQueue.EnqueueTimer(TimeSpan.FromMilliseconds(rx_timeout), timeouted);
            m_fError = false;
            bool fTimeouted = false;
            
            //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TEST, String.Format("Receiver {0} started", rx_filter.transactionID));

            /* there are multiple incoming packets possible, if there wasn't a matching packet so far, 
             * wait for it as long there is no timout condition
             */
            while (!fTimeouted && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
            {
                yield return Arbiter.Choice(
                    Arbiter.Receive(false, timeouted, to =>
                    {
                        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_WARNING, String.Format("Receiver {0} Rx Timeout", rx_filter.transactionID));
                        fTimeouted = true;
                    }),
                    Arbiter.Receive(false, m_portWaitForRx, trigger =>
                    {
                        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TEST, String.Format("Receiver {0} released, error is {1}", rx_filter.transactionID, error));
                    }));

                    if (!fTimeouted && !m_fError)
                        while (m_Queue.Count != 0 && m_ReloadConfig.State < ReloadConfig.RELOAD_State.Exit)
                        {
                            
                            ReloadFLMEventArgs args = (ReloadFLMEventArgs)m_Queue.Dequeue();

                            if (args == null || args.Message == null)
                            {
                                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Receive: Args == null"));
                                break;
                            }

                            if (args.Eventtype == ReloadFLMEventArgs.ReloadFLMEventTypes.RELOAD_EVENT_RECEIVE_OK)
                            {
                                ReloadMessage reloadMsg = args.Message;
                                
                                if (reloadMsg == null)
                                {
                                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Receive: Dropping invalid packet from {0}", args.ConnectionTableEntry != null ? args.ConnectionTableEntry.NodeID.ToString() : "unknown"));
                                    break;
                                }
                                //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("Receiver {0} Queuecount {1} Transactionid: {2}", rx_filter.transactionID, m_Queue.Count, reloadMsg.TransactionID));
                                if (reloadMsg.IsFragmented() && reloadMsg.IsSingleFragmentMessage() == false && rx_filter != null && reloadMsg.TransactionID == rx_filter.transactionID) 
                                {
                                  ReloadMessage reassembledMsg = null;
                                  lock (fragmentedMessageBuffer) {
                                    reassembledMsg = reloadMsg.ReceiveFragmentedMessage(ref fragmentedMessageBuffer);
                                  }
                                    if (reassembledMsg == null) //not yet all fragments received => not reassembled
                                    {
                                      Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessageFilter, int>(rx_filter, rx_timeout, Receive));
                                      m_TimeStart = DateTime.Now;
                                      yield break;
                                    }
                                    else
                                      reloadMsg = reassembledMsg; //message reassembled => continue as usual
                                }

                                if (args.ConnectionTableEntry != null)
                                    m_SourceNodeID = args.ConnectionTableEntry.NodeID;
                                else
                                    m_SourceNodeID = reloadMsg.LastHopNodeId;

                                if (rx_filter != null)
                                {
                                    //                              m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TEST, String.Format("Receiver {0} Checking against {1}", rx_filter.transactionID, reloadMsg.TransactionID));

                                    /* Most important part: Do only accept messages with the same transaction id
                                     * this ReloadDialog had been registered to and ignore the rest                */
                                    if (reloadMsg.TransactionID == rx_filter.transactionID)
                                    {
                                        if (!reloadMsg.IsRequest())
                                        {
                                            //m_ReceivedMessage = args.Message;
                                            m_ReceivedMessage = reloadMsg; //--joscha
                                            /* just a trick to get out of the parent loop */
                                            fTimeouted = true;
                                            break;
                                        }
                                        else
                                        {
                                          /* Our Request looped back to us, module Forwarding is handling this */

                                          if (reloadMsg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Error) {
                                            if (((ErrorResponse)reloadMsg.reload_message_body).ErrorCode == RELOAD_ErrorCode.Error_Not_Found) { // --joscha not found response => don't wait for timeout
                                              m_ReceivedMessage = null;
                                              /* just a trick to get out of the parent loop */
                                              fTimeouted = true;
                                              break;
                                            }
                                          }
                                        }
                                    }
                                }
                                else
                                {
                                    /* No filter specified, deliver every packet */
                                    //m_ReceivedMessage = args.Message;
                                    m_ReceivedMessage = reloadMsg; //--joscha;
                                    /* just a trick to get out of the parent loop */
                                    fTimeouted = true;
                                    break;
                                }
                            }
                        }
            }
            m_Transport.ReloadFLMEventHandler -= OVL_ReloadForwardLinkManagementEventHandler;
            m_fDone.Post(true);
        }

        /// <summary>
        /// TASK: Execute this dialog by sending the buffer and start reception.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="rx_filter">The rx_filter.</param>
        /// <param name="rx_timeout">The rx_timeout.</param>
        /// <returns></returns>
        public IEnumerator<ITask> Execute(ReloadMessage reloadMessage, ReloadMessageFilter rx_filter, int rx_timeout) {
            //m_Transport.Send(this.m_NextHopNode, reloadMessage);
            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<Node, ReloadMessage>(this.m_NextHopNode, reloadMessage, m_Transport.Send));
            m_TimeStart = DateTime.Now;
            //m_MessageFilter = rx_filter;
            Arbiter.Activate(m_DispatcherQueue, new IterativeTask<ReloadMessageFilter, int>(rx_filter, rx_timeout, Receive));
            yield break;
        }
    }
}
