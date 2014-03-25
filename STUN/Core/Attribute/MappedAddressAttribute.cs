using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class MappedAddressAttribute : STUNAttribute
    {
        private IPEndPoint m_MappedAddress = null;

        public IPEndPoint MappedAddress { get { return m_MappedAddress; } }

                
        public static MappedAddressAttribute CreateMappedAddress(String IpAddress, UInt16 Port)
        {
            MappedAddressAttribute mappedAddressAttribute = new MappedAddressAttribute();

            mappedAddressAttribute.m_MappedAddress = new IPEndPoint(IPAddress.Parse(IpAddress), Port);

            mappedAddressAttribute.m_Type = StunAttributeType.MappedAddress;
            mappedAddressAttribute.m_Value = mappedAddressAttribute.MappedAddressToByteArray();
            mappedAddressAttribute.m_Length = (UInt16)mappedAddressAttribute.m_Value.Length;                           // m_Value.Length passt hier, weil m_Value genau in ein Vielfaches von 4 Byte ist


            return mappedAddressAttribute;
        }

        private Byte[] MappedAddressToByteArray()
        {

            /*                     
               0                   1                   2                   3
               0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
              +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
              |0 0 0 0 0 0 0 0|    Family     |           Port                |
              +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
              |                                                               |
              |                 Address (32 bits or 128 bits)                 |
              |                                                               |
              +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                       Figure 5: Format of MAPPED-ADDRESS Attribute
             */


            // IPv4
            if (m_MappedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 32 Bit Family + Port und 32 Bit Address = 64 Bit = 8 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] mappedAddress = new Byte[8];

                int offset = 0;

                mappedAddress[offset++] = 0;
                mappedAddress[offset++] = (Byte)AddressFamily.IPv4;

                // Port
                //Byte[] port = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_MappedAddress.Port));
                Byte[] port = NetworkByteArray.CreateUInt16((UInt16)m_MappedAddress.Port);
                Array.Copy(port, 0, mappedAddress, offset, port.Length);
                offset += port.Length;

                Byte[] ipaddress = m_MappedAddress.Address.GetAddressBytes();
                Array.Copy(ipaddress, 0, mappedAddress, offset, ipaddress.Length);

                return mappedAddress;
            }


            // IPv6
            if (m_MappedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // 32 Bit Family + Port und 128 Bit Address = 160 Bit = 20 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] mappedAddress = new Byte[20];

                int offset = 0;

                mappedAddress[offset++] = 0;
                mappedAddress[offset++] = (Byte)AddressFamily.IPv6;

                // Port
                //Byte[] port = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_MappedAddress.Port));
                Byte[] port = NetworkByteArray.CreateUInt16((UInt16)m_MappedAddress.Port);
                Array.Copy(port, 0, mappedAddress, offset, port.Length);
                offset += port.Length;

                Byte[] ipaddress = m_MappedAddress.Address.GetAddressBytes();
                Array.Copy(ipaddress, 0, mappedAddress, offset, ipaddress.Length);

                return mappedAddress;
            }


            else
                return null;

        }

        public void ParseMappedAddress(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.MappedAddress;
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
            //m_MappedAddress.Port = port;


            // IPv4
            if (family == (Byte)AddressFamily.IPv4)
            {

                Byte[] address = new Byte[4];
                Array.Copy(m_Value, offset, address, 0, address.Length);

                m_MappedAddress = new IPEndPoint(new IPAddress(address), port);

            }

            // IPv6
            if (family == (Byte)AddressFamily.IPv6)
            {
                Byte[] address = new Byte[16];
                Array.Copy(m_Value, offset, address, 0, address.Length);

                m_MappedAddress = new IPEndPoint(new IPAddress(address), port);
            }
        }
    }
}
