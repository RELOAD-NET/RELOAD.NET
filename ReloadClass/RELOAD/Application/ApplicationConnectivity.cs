using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TSystems.RELOAD.Usage;

namespace TSystems.RELOAD.Application // --arc
{
    // RFC 6940 14.5. Application-ID
    // Code points in the range 61441 to 65534 are reserved for private use
    // IANA: http://www.iana.org/assignments/reload/reload.xhtml#app-id --arc
    public enum Application_ID
    {
        INVALID,
        IMAGE_STORE = 61441,
        SIP_REGISTRATION
        // ...
    }

    /// <summary>
    /// Application-Layer connectivity
    /// TODO: Use a State Pattern for the Application Logic
    /// </summary>
    public class ApplicationConnectivity
    {
        #region Properties
        private ReloadConfig m_ReloadConfig; // used for logger

        private IUsage m_usage;

        public IUsage Usage
        {
            get { return m_usage; }
            set { m_usage = value; }
        }

        private UInt16 m_applicationID;

        /// <summary>
        /// ApplicationID from AppAttachReq / AppAttachAns
        /// RFC 6.5.2 The AppAttach request and its response contain an application attribute,
        /// which indicates what protocol is to be run over the connection
        /// </summary>
        public UInt16 ApplicationID
        {
            get { return m_applicationID; }
            set { m_applicationID = value; }
        }

        private ReloadTLSServer m_TlsServer;

        /// <summary>
        /// Controlling Agents result from AppAttach is an ReloadTLSServer 
        /// TlsServer is used for application layer communication
        /// </summary>
        public ReloadTLSServer TlsServer
        {
            get { return m_TlsServer; }
            set { m_TlsServer = value; }
        }

        private ReloadTLSClient m_TlsClient;

        /// <summary>
        /// Controlled Agents result from AppAttach is an ReloadTLSClient 
        /// TlsClient is used for application layer communication
        /// </summary>
        public ReloadTLSClient TlsClient
        {
            get { return m_TlsClient; }
            set { m_TlsClient = value; }
        }
        #endregion

        public ApplicationConnectivity(ReloadConfig config, IUsage usage, UInt16 application)
        {
            this.m_ReloadConfig = config;
            this.m_usage = usage;
            this.ApplicationID = application;
        }

        public void ApplicationProcedure(IAssociation association)
        {
            if(Usage != null) // --> controlling agent
            {
                // With ((ImageStoreUsage)Usage).Data you have all the information of the fetched usage
                // For example a ImageStoreUsage can contain:
                // Album-Name, Resource-Name 
                // and each Client of the p2p image-share-app saves its images like /%APP-DIR%/App-Data/<Album-Name>/<Resource-Name>

                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "Controlling Agent starting application layer communication over direct conection");

                //switch(ApplicationID)
                //{
                //    case (UInt16)Application_ID.IMAGE_STORE:
                        ImageStoreApplication app = new ImageStoreApplication();
                        app.ControllingAgentAppProtocol(association, (ImageStoreUsage)this.Usage, this.m_ReloadConfig); // controlling agent app specific protocol
                //        break;

                //    case (UInt16)Application_ID.SIP_REGISTRATION:
                //        break;

                //    case (UInt16)Application_ID.INVALID:
                //        break;

                //    default:
                //        break;
                //}
            }
            else // --> controlled agent
            {
                Usage = new ImageStoreUsage(m_ReloadConfig.ThisMachine.UsageManager);
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, "TLS Client starting application layer communication over direct conection");

                UInt16 app_id = this.ApplicationID;
                switch (ApplicationID)
                {
                    case (UInt16)Application_ID.IMAGE_STORE:
                        ImageStoreApplication app = new ImageStoreApplication();
                        app.ControlledAgentAppProtocol(association, (ImageStoreUsage)this.Usage, this.m_ReloadConfig); // controlled agent app specific protocol
                        break;

                    case (UInt16)Application_ID.SIP_REGISTRATION:
                        break;

                    case (UInt16)Application_ID.INVALID:
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
