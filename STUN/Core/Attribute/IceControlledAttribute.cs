using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class IceControlledAttribute : STUNAttribute
    {
        private UInt64 m_IceControlled = 0;

        public UInt64 IceControlled { get { return m_IceControlled; } }


        public static IceControlledAttribute CreateIceControlled()
        {
            IceControlledAttribute iceControlledAttribute = new IceControlledAttribute();

            iceControlledAttribute.m_IceControlled = Misc.GetRandomUInt64();

            iceControlledAttribute.m_Type = StunAttributeType.IceControlled;
            iceControlledAttribute.m_Value = iceControlledAttribute.IceControlledToByteArray();
            iceControlledAttribute.m_Length = (UInt16)iceControlledAttribute.m_Value.Length;      // m_Value.Length passt hier, weil m_Value 8 Bytes groß ist

            return iceControlledAttribute;
        }


        private Byte[] IceControlledToByteArray()
        {
            return NetworkByteArray.CreateUInt64(m_IceControlled);
        }


        public void ParseIceControlled(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.IceControlled;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_IceControlled = NetworkByteArray.ReadUInt64(m_Value, 0);
        }

    }
}
