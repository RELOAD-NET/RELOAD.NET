using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class PriorityAttribute : STUNAttribute
    {
        private UInt32 m_Priority = 0;

        public UInt32 Priority { get { return m_Priority; } }


        public static PriorityAttribute CreatePriority(UInt32 Priority)
        {
            PriorityAttribute priorityAttribute = new PriorityAttribute();

            priorityAttribute.m_Priority = Priority;

            priorityAttribute.m_Type = StunAttributeType.Priority;
            priorityAttribute.m_Value = priorityAttribute.PriorityToByteArray();        
            priorityAttribute.m_Length = (UInt16)priorityAttribute.m_Value.Length;      // m_Value.Length passt hier, weil m_Value 4 Bytes groß ist
            
            return priorityAttribute;
        }

        private Byte[] PriorityToByteArray()
        {
            return NetworkByteArray.CreateUInt32(m_Priority);
        }

        public void ParsePriority(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Priority;
            m_Length = att_length;
            m_Value = att_value_with_padding;
                        
            m_Priority = NetworkByteArray.ReadUInt32(m_Value, 0);
        }

    }
}
