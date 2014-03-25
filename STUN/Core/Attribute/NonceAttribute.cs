using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class NonceAttribute : STUNAttribute
    {
        private String m_Nonce = null;

        public String Nonce { get { return m_Nonce; } }

        
        public static NonceAttribute CreateNonce(String Nonce)
        {
            NonceAttribute nonceAttribute = new NonceAttribute();

            nonceAttribute.m_Nonce = Nonce;

            nonceAttribute.m_Type = StunAttributeType.Nonce;

            nonceAttribute.m_Value = nonceAttribute.NonceToByteArray();

            // durch die UTF8 Kodierung entsteht eine andere Länge als die des Strings m_Nonce.Length
            nonceAttribute.m_Length = (UInt16)Misc.GetUTF8LengthFromString(nonceAttribute.m_Nonce);

            return nonceAttribute;

        }

        private Byte[] NonceToByteArray()
        {
            Byte[] nonce;
            int nonceUTF8length = Misc.GetUTF8LengthFromString(m_Nonce);

            // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
            if ((nonceUTF8length % 4) == 0)
                nonce = new Byte[nonceUTF8length];

            // falls nicht werden zusätzliche Bytes hinzugefügt
            else
                nonce = new Byte[nonceUTF8length + (4 - (nonceUTF8length % 4))];

            // keine Vorschrift für Codierung in RFC, deshalb hier UTF8
            Array.Copy(Encoding.UTF8.GetBytes(m_Nonce), 0, nonce, 0, nonceUTF8length);

            return nonce;
        }

        public void ParseNonce(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Nonce;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            // keine Vorschrift für Codierung in RFC, deshalb hier UTF8
            m_Nonce = Encoding.UTF8.GetString(att_value_with_padding, 0, att_length);

        }

    }
}
