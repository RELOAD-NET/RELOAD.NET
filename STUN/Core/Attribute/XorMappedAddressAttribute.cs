using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class XorMappedAddressAttribute : STUNAttribute
    {
        private IPEndPoint m_XorMappedAddress = null;

        public IPEndPoint XorMappedAddress { get { return m_XorMappedAddress; } }

        
        public static XorMappedAddressAttribute CreateXorMappedAddress(Byte[] TransactionID, String IpAddress, UInt16 Port)
        {
            XorMappedAddressAttribute xorMappedAddressAttribute = new XorMappedAddressAttribute();

            xorMappedAddressAttribute.m_XorMappedAddress = new IPEndPoint(IPAddress.Parse(IpAddress), Port);

            xorMappedAddressAttribute.m_Type = StunAttributeType.XorMappedAddress;
            xorMappedAddressAttribute.m_Value = xorMappedAddressAttribute.XorMappedAddressToByteArray(TransactionID);
            xorMappedAddressAttribute.m_Length = (UInt16)xorMappedAddressAttribute.m_Value.Length;                           // m_Value.Length passt hier, weil m_Value genau in ein Vielfaches von 4 Byte ist


            return xorMappedAddressAttribute;
        }

        private Byte[] XorMappedAddressToByteArray(Byte[] transactionID)
        {

            /*
              0                   1                   2                   3
              0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
             +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
             |x x x x x x x x|    Family     |         X-Port                |
             +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
             |                X-Address (Variable)
             +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                     Figure 6: Format of XOR-MAPPED-ADDRESS Attribute
            */

            // IPv4
            if (m_XorMappedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 32 Bit Family + Port und 32 Bit Address = 64 Bit = 8 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] xorMappesAddress = new Byte[8];

                int offset = 0;

                xorMappesAddress[offset++] = 0;
                xorMappesAddress[offset++] = (Byte)AddressFamily.IPv4;


                // XPort
                //Byte[] xport = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)(m_XorMappedAddress.Port ^ 0x2112)));
                Byte[] xport = NetworkByteArray.CreateUInt16((UInt16)(m_XorMappedAddress.Port ^ 0x2112));
                Array.Copy(xport, 0, xorMappesAddress, offset, xport.Length);
                offset += xport.Length;



                Byte[] ipaddress = m_XorMappedAddress.Address.GetAddressBytes();
                Byte[] magicCookie = { 0x21, 0x12, 0xA4, 0x42 };                      // Magic Cookie: 0 x 21 12 A4 42
                Byte[] xipaddress = new Byte[4];

                for (int i = 0; i < 4; i++)
                    xipaddress[i] = (Byte)(ipaddress[i] ^ magicCookie[i]);

                Array.Copy(xipaddress, 0, xorMappesAddress, offset, xipaddress.Length);


                return xorMappesAddress;
            }


            // IPv6
            if (m_XorMappedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // 32 Bit Family + Port und 32 Bit Address = 64 Bit = 8 Byte (passt, da Vielfaches von 4 Byte)
                Byte[] xma = new Byte[20];

                int offset = 0;

                xma[offset++] = 0;
                xma[offset++] = (Byte)AddressFamily.IPv6;


                // XPort
                //Byte[] xport = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)(m_XorMappedAddress.Port ^ 0x2112)));
                Byte[] xport = NetworkByteArray.CreateUInt16((UInt16)(m_XorMappedAddress.Port ^ 0x2112));
                Array.Copy(xport, 0, xma, offset, xport.Length);
                offset += xport.Length;

                // IP Adresse (128 bit = 16 Byte)
                Byte[] ipaddress = m_XorMappedAddress.Address.GetAddressBytes();
                Byte[] magicCookie = { 0x21, 0x12, 0xA4, 0x42 };


                // Kette aus magic cookie (32 Bit = 4 Byte) und TransactionID (96 Bit = 12 Byte) = 128 Bit = 16 Byte 
                Byte[] concatenation = new Byte[16];
                Array.Copy(magicCookie, 0, concatenation, 0, 4);
                Array.Copy(transactionID, 0, concatenation, 32, 12);

                Byte[] xipaddress = new Byte[16];

                for (int i = 0; i < 16; i++)
                    xipaddress[i] = (Byte)(ipaddress[i] ^ concatenation[i]);

                Array.Copy(xipaddress, 0, xma, offset, xipaddress.Length);


                return xma;
            }

            else
                return null;
        }

        public void ParseXorMappedAddress(UInt16 att_length, Byte[] att_value_with_padding, Byte[] trans_id)
        {
            m_Type = StunAttributeType.XorMappedAddress;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            int offset = 0;

            // erstes Byte alles 0 => überspringen
            offset++;

            // Family parsen (1 Byte)
            Byte family = m_Value[offset];
            offset++;

            // Port parsen (2 Byte)
            //UInt16 xport = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(m_Value, offset));
            UInt16 xport = NetworkByteArray.ReadUInt16(m_Value, offset);
            offset += 2;

            // Port XORen
            UInt16 port = (UInt16)(xport ^ 0x2112);


            // IPv4
            if (family == (Byte)AddressFamily.IPv4)
            {

                Byte[] xaddress = new Byte[4];
                Array.Copy(m_Value, offset, xaddress, 0, xaddress.Length);

                Byte[] magicCookie = { 0x21, 0x12, 0xA4, 0x42 };                      // Magic Cookie: 0 x 21 12 A4 42
                Byte[] address = new Byte[4];

                // IP XORen
                for (int i = 0; i < 4; i++)
                    address[i] = (Byte)(xaddress[i] ^ magicCookie[i]);

                m_XorMappedAddress = new IPEndPoint(new IPAddress(address), port);

            }

            // IPv6
            if (family == (Byte)AddressFamily.IPv6)
            {
                Byte[] xaddress = new Byte[16];
                Array.Copy(m_Value, offset, xaddress, 0, xaddress.Length);

                Byte[] magicCookie = { 0x21, 0x12, 0xA4, 0x42 };


                // Kette aus magic cookie (32 Bit = 4 Byte) und TransactionID (96 Bit = 12 Byte) = 128 Bit = 16 Byte 
                Byte[] concatenation = new Byte[16];
                Array.Copy(magicCookie, 0, concatenation, 0, 4);
                Array.Copy(trans_id, 0, concatenation, 4, 12);

                Byte[] address = new Byte[16];

                for (int i = 0; i < 16; i++)
                    address[i] = (Byte)(xaddress[i] ^ concatenation[i]);

                m_XorMappedAddress = new IPEndPoint(new IPAddress(address), port);
            }
        }

    }
}
