using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class UnknownAttributesAttribute : STUNAttribute
    {
        private List<UInt16> m_UnknownAttributes = new List<UInt16>();

        public List<UInt16> UnknownAttributes { get { return m_UnknownAttributes; } }

        public static UnknownAttributesAttribute CreateUnknownAttributes(List<UInt16> listOfUnknownAttributes)
        {
            UnknownAttributesAttribute unknownAttributesAttribute = new UnknownAttributesAttribute();

            unknownAttributesAttribute.m_UnknownAttributes = listOfUnknownAttributes;

            unknownAttributesAttribute.m_Type = StunAttributeType.UnknownAttributes;

            unknownAttributesAttribute.m_Value = unknownAttributesAttribute.UnknownAttributesToByteArray();

            // ein Attribut ist 16 Bit (2 Byte) groß
            unknownAttributesAttribute.m_Length = (UInt16)(listOfUnknownAttributes.Count * 2);

            return unknownAttributesAttribute;
        }

        private Byte[] UnknownAttributesToByteArray()
        {
            int offset = 0;

            // ein Attribut hat 16 Bit (2 Byte)
            Byte[] unknownAttributes;

            // gerade Anzahl => passt
            if ((m_UnknownAttributes.Count % 2) == 0)
                unknownAttributes = new Byte[m_UnknownAttributes.Count * 2];

            // andernfalls Padding (2 Byte mehr)
            else
                unknownAttributes = new Byte[(m_UnknownAttributes.Count * 2) + 2];



            foreach (UInt16 attType in m_UnknownAttributes)
            {
                //Byte[] type = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)attType));
                Byte[] type = NetworkByteArray.CreateUInt16(attType);
                Array.Copy(type, 0, unknownAttributes, offset, type.Length);
                offset += type.Length;
            }

            return unknownAttributes;
        }

        public void ParseUnknownAttributes(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.UnknownAttributes;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            int offset = 0;

            for (int i = 0; i < (m_Length / 2); i++)
            {
                //m_UnknownAttributes.Add((UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(m_Value, offset)));
                m_UnknownAttributes.Add(NetworkByteArray.ReadUInt16(m_Value, offset));
                offset += 2;
            }
        }

    }
}
