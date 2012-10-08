using System;

#region [ Some missing classes ]
namespace System
{
    [AttributeUsageAttribute(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false)]
    public sealed class SerializableAttribute : Attribute
    {
    }
}

namespace System
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class NonSerializedAttribute : Attribute
    {
    }
}

namespace System.ComponentModel
{
    [AttributeUsageAttribute(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class DesignerCategoryAttribute : Attribute
    {
        public DesignerCategoryAttribute(String foo)
        {
        }
    }
}

namespace System.Xml
{
    public class XmlAttribute
    {
        // Wird in Configuration.cs für anyAttrField und AnyAttr verwendet.
    }

    namespace Serialization
    {
        [AttributeUsageAttribute(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
        public class XmlAnyAttributeAttribute : Attribute
        {
            // Wird in Configuration.cs für AnyAttr verwendet.
        }
    }
}

namespace System.Net.Security
{
}

namespace System.Security.Cryptography
{
    public class SHA1CryptoServiceProvider : SHA1Managed
    {
    }
}

namespace System.Web.Script.Serialization
{
    public class JavaScriptSerializer
    {
        public string Serialize(Object value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
    }
}
#endregion

#region [ Generic collection stuff ]
namespace System.Collections
{
    public class Queue : Generic.Queue<Object>
    {
    }
}

namespace System.Collections.Generic
{
    public class SortedDictionary<TKey, TValue> : C5.TreeDictionary<TKey, TValue>
    {
    }
}

namespace System.Collections.Generic
{
    public class SortedList<TKey, TValue> : C5.TreeDictionary<TKey, TValue>
    {
    }
}
#endregion

#region [ Encoding surrogate class ]
namespace System.Text
{
    public class ASCIIEncoding
    {
        public byte[] GetBytes(String value)
        {
            byte[] result = new byte[value.Length];

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                result[i] = character <= 0x7f ? (byte)character : (byte)'?';
            }

            return result;
        }

        public string GetString(byte[] bytes, int byteIndex, int byteCount)
        {
            StringBuilder result = new StringBuilder(byteCount);

            for (int i = byteIndex; i < (byteIndex + byteCount); i++)
            {
                byte character = bytes[i];
                result.Append(character <= 0x7F ? (char)character : '?');
            }

            return result.ToString();
        }
    }

    public static class Encoding
    {
        public static ASCIIEncoding Default { get { return new ASCIIEncoding(); } }
        public static ASCIIEncoding ASCII { get { return new ASCIIEncoding(); } }
        public static UTF8Encoding UTF8 { get { return new UTF8Encoding(); } }
        public static UnicodeEncoding Unicode { get { return new UnicodeEncoding(); } }
    }

    /// <summary>
    /// Extends encoding classes by the GetString(byte[]) method.
    /// </summary>
    public static class EncodingExtensions
    {
        public static string GetString(this ASCIIEncoding encoding, byte[] bytes)
        {
            return encoding.GetString(bytes, 0, bytes.Length);
        }

        public static string GetString(this UTF8Encoding encoding, byte[] bytes)
        {
            return encoding.GetString(bytes, 0, bytes.Length);
        }

        public static string GetString(this UnicodeEncoding encoding, byte[] bytes)
        {
            return encoding.GetString(bytes, 0, bytes.Length);
        }
    }
}
#endregion

#region [ File surrogate class ]
namespace System.IO
{
    using System.IO.IsolatedStorage;

    public static class File
    {
        public static StreamWriter AppendText(string path)
        {
            return new StreamWriter(path, true);
        }

        public static byte[] ReadAllBytes(string path)
        {
            byte[] buffer;

            using (FileStream fs = IsolatedStorageFile.GetUserStoreForApplication()
                .OpenFile(path, FileMode.OpenOrCreate))
            {
                int offset = 0;
                int count = (int)fs.Length;
                buffer = new byte[count];
                while (count > 0)
                {
                    int bytesRead = fs.Read(buffer, offset, count);
                    offset += bytesRead;
                    count -= bytesRead;
                }
            }

            return buffer;
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");

            using (FileStream fs = IsolatedStorageFile.GetUserStoreForApplication()
                .OpenFile(path, FileMode.Create))
            {
                fs.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
#endregion

#region [ HttpWebRequest extension methods ]
namespace System.Net
{
    using System.IO;
    using System.Threading;

    public static class HttpWebRequestExtensions
    {
        //private struct HttpWebRequestState
        //{
        //    public HttpWebRequest Request { get; set; }
        //    public Stream RequestStream { get; set; }
        //    public HttpWebResponse Response { get; set; }
        //}

        #region HttpWebRequest.GetResponse()
        public static HttpWebResponse GetResponse(this HttpWebRequest request)
        {
            var responseReadyEvent = new AutoResetEvent(false);
            HttpWebResponse response = null;

            var callback = new AsyncCallback(delegate(IAsyncResult asynchronousResult)
            {
                response = (HttpWebResponse)request.EndGetResponse(asynchronousResult);
                responseReadyEvent.Set();
            });

            request.BeginGetResponse(callback, request);
            responseReadyEvent.WaitOne();

            return response;
        }

        //public static WebResponse GetResponse(this HttpWebRequest request)
        //{
        //    HttpWebRequestState asyncState = new HttpWebRequestState();
        //    asyncState.Request = request;

        //    IAsyncResult asyncResult = (IAsyncResult)request.BeginGetResponse(new AsyncCallback(GetResponseCallback), asyncState);
        //    asyncResult.AsyncWaitHandle.WaitOne();

        //    return asyncState.Response;
        //}

        //private static void GetResponseCallback(IAsyncResult asyncResult)
        //{
        //    HttpWebRequestState asyncState = (HttpWebRequestState)asyncResult.AsyncState;
        //    HttpWebRequest request = asyncState.Request;

        //    asyncState.Response = (HttpWebResponse)request.EndGetResponse(asyncResult);
        //}
        #endregion

        #region HttpWebRequest.GetRequestStream()
        public static Stream GetRequestStream(this HttpWebRequest request)
        {
            var streamReadyEvent = new AutoResetEvent(false);
            Stream stream = null;

            var callback = new AsyncCallback(delegate(IAsyncResult asynchronousResult)
            {
                stream = (Stream)request.EndGetRequestStream(asynchronousResult);
                streamReadyEvent.Set();
            });

            request.BeginGetRequestStream(callback, request);
            streamReadyEvent.WaitOne();

            return stream;
        }

        //public static Stream GetRequestStream(this HttpWebRequest request)
        //{
        //    HttpWebRequestState asyncState = new HttpWebRequestState();
        //    asyncState.Request = request;

        //    IAsyncResult asyncResult = (IAsyncResult)request.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallback), asyncState);
        //    asyncResult.AsyncWaitHandle.WaitOne();

        //    return asyncState.RequestStream;
        //}

        //private static void GetRequestStreamCallback(IAsyncResult asyncResult)
        //{
        //    HttpWebRequestState asyncState = (HttpWebRequestState)asyncResult.AsyncState;
        //    HttpWebRequest request = asyncState.Request;

        //    asyncState.RequestStream = request.EndGetRequestStream(asyncResult);
        //}
        #endregion
    }
}
#endregion

#region [ Socket extension methods ]
namespace System.Net.Sockets
{
    [FlagsAttribute]
    public enum SocketFlags
    {
        None

        // TODO:
        // If you need more socket flags,
        // just implement them here.
    }

    internal sealed class SocketAsyncResult : IAsyncResult
    {
        public object AsyncState { get; set; }
        public Threading.WaitHandle AsyncWaitHandle { get; set; }
        public bool CompletedSynchronously { get; set; }
        public bool IsCompleted { get; set; }

        public AsyncCallback Callback { get; set; }
    }

    public static class SocketExtensions
    {
        #region [ Socket.BeginConnect() / Socket.EndConnect() ]
        public static IAsyncResult BeginConnect(this Socket socket, EndPoint remoteEP, AsyncCallback callback, Object state)
        {
            SocketAsyncResult asyncResult = new SocketAsyncResult();
            asyncResult.Callback = callback;

            SocketAsyncEventArgs e = new SocketAsyncEventArgs();
            e.RemoteEndPoint = remoteEP;
            e.UserToken = asyncResult;
            e.Completed += new EventHandler<SocketAsyncEventArgs>(BeginConnectCompleted);

            socket.ConnectAsync(e);
            return asyncResult;
        }

        public static void EndConnect(this Socket socket, IAsyncResult asyncResult)
        {
        }

        private static void BeginConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            SocketAsyncResult asyncResult = (SocketAsyncResult)e.UserToken;

            AsyncCallback callback = asyncResult.Callback;
            callback(asyncResult);
        }
        #endregion

        #region [ Socket.BeginSend() / Socket.EndSend() ]
        public static IAsyncResult BeginSend(this Socket socket, byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, Object state)
        {
            SocketAsyncResult asyncResult = new SocketAsyncResult();
            asyncResult.Callback = callback;
            asyncResult.AsyncState = state;

            SocketAsyncEventArgs e = new SocketAsyncEventArgs();
            //e.RemoteEndPoint = socket.RemoteEndPoint;
            e.SetBuffer(buffer, offset, size);
            e.UserToken = asyncResult;
            e.Completed += new EventHandler<SocketAsyncEventArgs>(BeginSendCompleted);

            socket.SendAsync(e);
            return null;
        }

        public static int EndSend(this Socket socket, IAsyncResult asyncResult)
        {
            // FIXME: Es sollte eigentlich die Anzahl der übertragenen Bytes zurückgegeben werden
            return -1;
        }

        private static void BeginSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            SocketAsyncResult asyncResult = (SocketAsyncResult)e.UserToken;
            //asyncResult.AsyncState = e.BytesTransferred;

            AsyncCallback callback = asyncResult.Callback;
            callback(asyncResult);
        }
        #endregion

        #region [ Socket.BeginReceive() / Socket.EndReceive() ]
        public static IAsyncResult BeginReceive(this Socket socket, byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, Object state)
        {
            SocketAsyncResult asyncResult = new SocketAsyncResult();
            asyncResult.Callback = callback;

            SocketAsyncEventArgs e = new SocketAsyncEventArgs();
            //e.RemoteEndPoint = socket.RemoteEndPoint;
            e.SetBuffer(buffer, offset, size);
            e.UserToken = asyncResult;
            e.Completed += new EventHandler<SocketAsyncEventArgs>(BeginReceiveCompleted);

            socket.ReceiveAsync(e);
            return null;
        }

        public static int EndReceive(this Socket socket, IAsyncResult asyncResult)
        {
            return (int)asyncResult.AsyncState;
        }

        private static void BeginReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            SocketAsyncResult asyncResult = (SocketAsyncResult)e.UserToken;
            asyncResult.AsyncState = e.BytesTransferred;

            AsyncCallback callback = asyncResult.Callback;
            callback(asyncResult);
        }
        #endregion
    }
}
#endregion

#region [ Basic DNS implementation ]
namespace System.Net
{
    using System.Collections.Generic;

    public class IPHostEntry
    {
        public IPAddress[] AddressList { get; set; }

        public IPHostEntry(IPAddress[] ipAdressList)
        {
            AddressList = ipAdressList;
        }
    }

    public static class Dns
    {
        public static String GetHostName()
        {
            return "test";
        }

        /// <summary>
        /// Faked DNS lookup.
        /// </summary>
        public static IPHostEntry GetHostEntry(string hostname)
        {
            List<IPAddress> ipAdressList = new List<IPAddress>();

            if (hostname.Equals(GetHostName()))
            {
                ipAdressList.Add(new IPAddress(
                    new byte[] { 141, 19, 96, 69 }));
            }

            return new IPHostEntry(ipAdressList.ToArray());
        }
    }
}
#endregion