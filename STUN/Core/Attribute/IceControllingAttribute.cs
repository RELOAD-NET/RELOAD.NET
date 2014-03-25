using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class IceControllingAttribute : STUNAttribute
    {
        private UInt64 m_IceControlling = 0;

        public UInt64 IceControlling { get { return m_IceControlling; } }


        public static IceControllingAttribute CreateIceControlling()
        {
            IceControllingAttribute iceControllingAttribute = new IceControllingAttribute();

            iceControllingAttribute.m_IceControlling = Misc.GetRandomUInt64();

            iceControllingAttribute.m_Type = StunAttributeType.IceControlling;
            iceControllingAttribute.m_Value = iceControllingAttribute.IceControllingToByteArray();
            iceControllingAttribute.m_Length = (UInt16)iceControllingAttribute.m_Value.Length;      // m_Value.Length passt hier, weil m_Value 8 Bytes groß ist

            return iceControllingAttribute;
        }


        private Byte[] IceControllingToByteArray()
        {
            return NetworkByteArray.CreateUInt64(m_IceControlling);
        }


        public void ParseIceControlling(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.IceControlling;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_IceControlling = NetworkByteArray.ReadUInt64(m_Value, 0);
        }
    }
}
