using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class UsernameAttribute : STUNAttribute
    {
        private String m_Username = null;               // SASLPrep'ed

        public String Username { get { return m_Username; } }

        
        public static UsernameAttribute CreateUsername(String Username)
        {
            UsernameAttribute usernameAttribute = new UsernameAttribute();

            usernameAttribute.m_Username = SASLPrep.STUNSASLPrep(Username);

            usernameAttribute.m_Type = StunAttributeType.Username;

            usernameAttribute.m_Value = usernameAttribute.UsernameToByteArray();

            // durch die UTF8 Kodierung entsteht eine andere Länge als die des Strings m_Username.Length
            usernameAttribute.m_Length = (UInt16)Misc.GetUTF8LengthFromString(usernameAttribute.m_Username);

            return usernameAttribute;
        }

        private Byte[] UsernameToByteArray()
        {
            Byte[] username;
            int usernameUTF8length = Misc.GetUTF8LengthFromString(m_Username);

            // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
            if ((usernameUTF8length % 4) == 0)
                username = new Byte[usernameUTF8length];

            // falls nicht werden zusätzliche Bytes hinzugefügt
            else
                username = new Byte[usernameUTF8length + (4 - (usernameUTF8length % 4))];

            // Byte Array muss UTF-8 codiert sein
            Array.Copy(Encoding.UTF8.GetBytes(m_Username), 0, username, 0, usernameUTF8length);

            return username;
        }

        public void ParseUsername(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Username;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_Username = Encoding.UTF8.GetString(att_value_with_padding, 0, att_length);

        }
    }
}
