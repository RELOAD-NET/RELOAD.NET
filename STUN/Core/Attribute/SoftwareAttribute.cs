using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class SoftwareAttribute : STUNAttribute
    {
        private String m_Software = null;

        public String Software { get { return m_Software; } }

        public static SoftwareAttribute CreateSoftware(String Software)
        {
            SoftwareAttribute softwareAttribute = new SoftwareAttribute();

            softwareAttribute.m_Software = Software;

            softwareAttribute.m_Type = StunAttributeType.Software;

            softwareAttribute.m_Value = softwareAttribute.SoftwareToByteArray();

            // durch die UTF8 Kodierung entsteht eine andere Länge als die des Strings m_Software.Length
            softwareAttribute.m_Length = (UInt16)Misc.GetUTF8LengthFromString(softwareAttribute.m_Software);

            return softwareAttribute;
        }

        private Byte[] SoftwareToByteArray()
        {
            Byte[] software;
            int softwareUTF8length = Misc.GetUTF8LengthFromString(m_Software);

            // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
            if ((softwareUTF8length % 4) == 0)
                software = new Byte[softwareUTF8length];

            // falls nicht werden zusätzliche Bytes hinzugefügt
            else
                software = new Byte[softwareUTF8length + (4 - (softwareUTF8length % 4))];

            // Byte Array muss UTF-8 codiert sein
            Array.Copy(Encoding.UTF8.GetBytes(m_Software), 0, software, 0, softwareUTF8length);

            return software;
        }

        public void ParseSoftware(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Software;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_Software = Encoding.UTF8.GetString(att_value_with_padding, 0, att_length);

        }
    }
}
