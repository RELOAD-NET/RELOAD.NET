using Microsoft.Ccr.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TSystems.RELOAD.Application
{
    // Events: http://msdn.microsoft.com/en-us/library/aa645739%28v=vs.71%29.aspx
    public class ReceiverEventArgs : EventArgs
    {
        private IAssociation m_association;

        public IAssociation Association
        {
            get { return m_association; }
            set { m_association = value; }
        }

        public ReceiverEventArgs(IAssociation a)
        {
            this.Association = a;
        }
    }

    public delegate void ReceivedDataHandler(object sender, ReceiverEventArgs e);

    /// <summary>
    /// TODO: Use a State Pattern for the Application Logic
    /// </summary>
    class ImageStoreApplication
    {
        public event ReceivedDataHandler Data;

        protected virtual void OnData(ReceiverEventArgs e)
        {
            if (Data != null)
                Data(this, e);
        }

        // Event-Handler 
        private void DataReceived(object sender, ReceiverEventArgs e)
        {
            IAssociation association = e.Association;

            byte[] recv_buffer = null;
            int buflen = association.InputBuffer.Length;
            recv_buffer = new byte[buflen];
            Buffer.BlockCopy(association.InputBuffer, 0, recv_buffer, 0, buflen);

            using(MemoryStream ms = new MemoryStream(recv_buffer, 0, buflen, false, true))
            {
                BinaryReader br = new BinaryReader(ms);

                int len = br.ReadInt32();
                string resourceName = new String(br.ReadChars(len));
                br.Close();
            }
        }

        /// <summary>
        /// Runs the Controlling Agents site of the specific application protocol 
        /// </summary>
        public void ControllingAgentAppProtocol(IAssociation association, ImageStoreUsage usage, ReloadConfig reloadConfig)
        {
            byte[] buffer = null;

            int buflen = usage.Data.Name.Length + 4;
            buffer = new byte[buflen];
            using (MemoryStream ms = new MemoryStream(buffer, 0, buflen, true, true))
            {
                BinaryWriter bw = new BinaryWriter(ms);

                int len = usage.Data.Name.Length;
                bw.Write(len);
                bw.Write(usage.Data.Name.ToCharArray());
                bw.Close();
            }

            EnqueueDataForWrite(association, buffer); //threadsafe

            // Debug with BinaryReader
            using (MemoryStream recv = new MemoryStream(buffer, 0, buflen, false, true))
            {
                BinaryReader br = new BinaryReader(recv);
                int recv_len = br.ReadInt32();
                string recv_resourceName = new String(br.ReadChars(recv_len));
                br.Close();
            }
        }

        /// <summary>
        /// Runs the Controlled Agents site of the specific application protocol
        /// </summary>
        public void ControlledAgentAppProtocol(IAssociation association, ImageStoreUsage usage, ReloadConfig reloadConfig)
        {
            // appReceive reads data as long as the connection is open
            Arbiter.Activate(reloadConfig.DispatcherQueue, new IterativeTask<IAssociation>(association, appReceive));

            this.Data += new ReceivedDataHandler(DataReceived); // Register event handler for received data
        }



        /***********************************************************************************************************
         * Thread-Save communication stuff (looked in TLS for reference)
         *

         * Send methods:
         */

        /// <summary>
        /// Enqueue the Data to Write in the ConcurrentQueue
        /// </summary>
        void EnqueueDataForWrite(IAssociation sender, byte[] buffer)
        {
            if (buffer == null)
                return;

            sender.WritePendingData.Enqueue(buffer);

            lock (sender.WritePendingData)
            {
                if (sender.SendingData)
                    return;
                else
                    sender.SendingData = true;

                Write(sender);
            }
        }


        /// <summary>
        /// Write Data in Queue to SslStream
        /// </summary>
        /// <param name="sender"></param>
        void Write(IAssociation sender)
        {

            byte[] buffer = null;

            try
            {
                if (sender.WritePendingData.Count > 0 && sender.WritePendingData.TryDequeue(out buffer))
                {

                        sender.AssociatedSslStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(socketAsyncSendCallback), sender);
                }
                else
                {
                    lock (sender.WritePendingData)
                    {
                        sender.SendingData = false;
                    }
                }
            }
            catch (Exception ex)
            {
                // TODO:
                //
                //if (ex is SocketException)
                //{
                //    HandleRemoteClosing(sender.AssociatedSslStream, sender.AssociatedClient);
                //}

                //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "ImageStoreApplication Write: " + ex.Message);

                //lock (sender.WritePendingData)
                //{
                //    sender.SendingData = false;
                //}
            }
        }

        /// <summary>
        /// Socket async send callback.
        /// </summary>
        /// <param name="ar">The ar.</param>
        private void socketAsyncSendCallback(IAsyncResult ar)
        {
            IAssociation association = (IAssociation)ar.AsyncState;
            try
            {
                association.AssociatedSslStream.EndWrite(ar);
            }
            catch (Exception ex)
            {
                // TODO:
                //
                //if (ex is SocketException)
                //    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_TRANSPORT, "AsyncSend SocketError:" + ((SocketException)ex).ErrorCode.ToString());
                //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "AsyncSend: " + ex.Message);
            }

            Write(association);
        }


        /**************************************************************************************************************
         * Receive methods:
         */
        /// <summary>
        /// TASK: Socket data reception for application protocol logic
        /// </summary>
        IEnumerator<ITask> appReceive(IAssociation association)
        {
            while (association.AssociatedClient.Connected) // TODO: think about abort condition here 
            {
                // Port<IAsyncResult>: It enqueues messages and keeps track of receivers that can consume messages.
                // IAsyncResult: Type for messages that can be enqueued - Represents the status of an asynchronous operation.
                var iarPort = new Port<IAsyncResult>();

                int bytesReceived = 0;

                // try to get the message
                try
                {
                    association.AssociatedSslStream.BeginRead(
                    association.InputBuffer,
                    association.InputBufferOffset,
                    association.InputBuffer.Length - association.InputBufferOffset,
                    iarPort.Post,
                    null);
                }
                catch (Exception ex)
                {
                    // TODO
                    //if (ex is SocketException)
                    //{
                    //    HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
                    //}
                    //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "Send: " + ex.Message);

                }

                // Creates a single item receiver
                // Execute handler on message arrival
                yield return Arbiter.Receive(false, iarPort, iar =>
                {
                    try
                    {
                        bytesReceived = association.AssociatedSslStream.EndRead(iar);
                    }
                    catch
                    {
                        bytesReceived = 0;
                    }
                });

                if (bytesReceived <= 0)
                {
                    // Close Socket if exception was thrown or stream is closed
                    //HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
                    //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("linkReceive: {0}, connection closed", association.AssociatedSslStream.GetHashCode()));
                    //break;

                    Thread.Yield();
                }

                association.InputBufferOffset += bytesReceived;
                while (association.InputBufferOffset > 0)
                {

                    int len = Math.Min(association.InputBuffer.Length, association.InputBufferOffset);
                    association.InputBufferOffset -= len;

                    //m_ReloadConfig.Statistics.BytesRx = (ulong)len;

                    if (len == 0)
                    {
                        association.InputBufferOffset = 0;
                        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, "SBB_OnReceive: Clearing association InputBuffer on MaxSize=0");
                    }

                    if (association.InputBufferOffset > 0)
                        Buffer.BlockCopy(association.InputBuffer, len, association.InputBuffer, 0, association.InputBufferOffset);

                    // Data received, now analyse it
                    if (association.AssociatedSocket.Connected)
                        OnData(new ReceiverEventArgs(association)); // fire event

                    else
                    {
                        // TODO
                        //HandleRemoteClosing(association.AssociatedSslStream, association.AssociatedClient);
                        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_BUG, String.Format("linkReceive: {0}, connection broken", association.GetHashCode()));
                        //break;
                    }
                }
            }
        }
    }
}
