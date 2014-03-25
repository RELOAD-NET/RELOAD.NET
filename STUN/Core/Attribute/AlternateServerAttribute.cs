using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class AlternateServerAttribute : STUNAttribute
    {
        private IPEndPoint m_AlternateServer = null;

        public IPEndPoint AlternateServer { get { return m_AlternateServer; } }

        public static AlternateServerAttribute CreateAlternateServer(String IpAddress, UInt16 Port)
        {
            AlternateServerAttribute alternateServerAttribute = new AlternateServerAttribute();

            alternateServerAttribute.m_AlternateServer = new IPEndPoint(IPAddress.Parse(IpAddress), Port);

            alternateServerAttribute.m_Type = StunAttributeType.AlternateServer;
            alternateServerAttribute.m_Value = alternateServerAttribute.AlternateServerToByteArray();
            alternateServerAttribute.m_Length = (UInt16)alternateServerAttribute.m_Value.Length;                           // m_Value.Length passt hier, weil m_Value genau in ein Vielfaches von 4 Byte ist


            return alternateServerAttribute;
        }

        private Byte[] AlternateServerToByteArray()
        {
            // HINWEIS: Verarbeitung exakt wie Mapped Address

            // IPv4
            if (m_AlternateServer.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 32 Bit Family + Port und 32 Bit Address = 64 Bit = 8 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] alternateServer = new Byte[8];

                int offset = 0;

                alternateServer[offset++] = 0;
                alternateServer[offset++] = (Byte)AddressFamily.IPv4;

                // Port
                //Byte[] port = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_AlternateServer.Port));
                Byte[] port = NetworkByteArray.CreateUInt16((UInt16)m_AlternateServer.Port);
                Array.Copy(port, 0, alternateServer, offset, port.Length);
                offset += port.Length;

                Byte[] ipaddress = m_AlternateServer.Address.GetAddressBytes();
                Array.Copy(ipaddress, 0, alternateServer, offset, ipaddress.Length);

                return alternateServer;
            }


            // IPv6
            if (m_AlternateServer.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // 32 Bit Family + Port und 128 Bit Address = 160 Bit = 20 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] alternateServer = new Byte[20];

                int offset = 0;

                alternateServer[offset++] = 0;
                alternateServer[offset++] = (Byte)AddressFamily.IPv6;

                // Port
                //Byte[] port = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_AlternateServer.Port));
                Byte[] port = NetworkByteArray.CreateUInt16((UInt16)m_AlternateServer.Port);
                Array.Copy(port, 0, alternateServer, offset, port.Length);
                offset += port.Length;

                Byte[] ipaddress = m_AlternateServer.Address.GetAddressBytes();
                Array.Copy(ipaddress, 0, alternateServer, offset, ipaddress.Length);

                return alternateServer;
            }


            else
                return null;
        }

        public void ParseAlternateServer(UInt16 att_length, Byte[] att_value_with_padding)
        {
            // HINWEIS !!! Verarbeitung extakt wie Mapped Address

            m_Type = StunAttributeType.AlternateServer;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            int offset = 0;

            // erstes Byte alles 0 => überspringen
            offset++;

            // Family parsen (1 Byte)
            Byte family = m_Value[offset];
            offset++;

            // Port parsen (2 Byte)
            //UInt16 port = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(m_Value, offset));
            UInt16 port = NetworkByteArray.ReadUInt16(m_Value, offset);
            offset += 2;


            // IPv4
            if (family == (Byte)AddressFamily.IPv4)
            {

                Byte[] address = new Byte[4];
                Array.Copy(m_Value, offset, address, 0, address.Length);

                m_AlternateServer = new IPEndPoint(new IPAddress(address), port);

            }

            // IPv6
            if (family == (Byte)AddressFamily.IPv6)
            {
                Byte[] address = new Byte[16];
                Array.Copy(m_Value, offset, address, 0, address.Length);

                m_AlternateServer = new IPEndPoint(new IPAddress(address), port);
            }
        }
    }
}
