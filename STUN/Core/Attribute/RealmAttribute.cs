using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class RealmAttribute : STUNAttribute
    {
        private String m_Realm = null;                  // SASLPrep'ed

        public String Realm { get { return m_Realm; } }

        
        public static RealmAttribute CreateRealm(String Realm)
        {
            RealmAttribute realmAttribute = new RealmAttribute();

            realmAttribute.m_Realm = SASLPrep.STUNSASLPrep(Realm);

            realmAttribute.m_Type = StunAttributeType.Realm;

            realmAttribute.m_Value = realmAttribute.RealmToByteArray();

            // durch die UTF8 Kodierung entsteht eine andere Länge als die des Strings m_Realm.Length
            realmAttribute.m_Length = (UInt16)Misc.GetUTF8LengthFromString(realmAttribute.m_Realm);

            return realmAttribute;
        }

        private Byte[] RealmToByteArray()
        {
            Byte[] realm;
            int realmUTF8length = Misc.GetUTF8LengthFromString(m_Realm);

            // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
            if ((realmUTF8length % 4) == 0)
                realm = new Byte[realmUTF8length];

            // falls nicht werden zusätzliche Bytes hinzugefügt
            else
                realm = new Byte[realmUTF8length + (4 - (realmUTF8length % 4))];

            // Byte Array muss UTF-8 codiert sein
            Array.Copy(Encoding.UTF8.GetBytes(m_Realm), 0, realm, 0, realmUTF8length);

            return realm;
        }

        public void ParseRealm(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Realm;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_Realm = Encoding.UTF8.GetString(att_value_with_padding, 0, att_length);

        }

    }
}
