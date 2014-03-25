using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class MessageIntegrityAttribute : STUNAttribute
    {
        private STUNMessageIntegrity m_MessageIntegrity = null;

        public STUNMessageIntegrity MessageIntegrity { get { return m_MessageIntegrity; } }


        public static MessageIntegrityAttribute CreateMessageIntegrity(Byte[] Key)
        {
            MessageIntegrityAttribute messageIntegrityAttribute = new MessageIntegrityAttribute();

            messageIntegrityAttribute.m_MessageIntegrity = new STUNMessageIntegrity(Key);

            messageIntegrityAttribute.m_Type = StunAttributeType.MessageIntegrity;
            messageIntegrityAttribute.m_Value = new Byte[20];                           // initial Dummy, Länge fest: 20 Byte
            messageIntegrityAttribute.m_Length = (UInt16)messageIntegrityAttribute.m_Value.Length;   // m_Value.Length passt hier, weil m_Value 20 Bytes groß ist (Vielfaches von 4 Byte)

            return messageIntegrityAttribute;
        }

        private Byte[] MessageIntegrityToByteArray()
        {
            // 20 Byte, passt
            return m_MessageIntegrity.Hash;
        }

        public void ParseMessageIntegrity(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.MessageIntegrity;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            m_MessageIntegrity = new STUNMessageIntegrity();
            Array.Copy(m_Value, 0, m_MessageIntegrity.Hash, 0, m_Length);
        }


        // Wert von Message Integrity aktualisieren
        public void UpdateMessageIntegrityValue()
        {
            m_Value = m_MessageIntegrity.Hash;
        }

        // Short Term Message Integrity
        public static MessageIntegrityAttribute CreateMessageIntegrityShortTerm(String Password)
        {
            String SASLPassword = SASLPrep.STUNSASLPrep(Password);

            // für die Wandlung des Strings in ein Byte Array sind keine Vorgaben im RFC zu finden
            // deshalb wird hier einfach UTF8 verwendet
            return CreateMessageIntegrity(Encoding.UTF8.GetBytes(SASLPassword));
        }

        // Long Term Message Integrity
        public static MessageIntegrityAttribute CreateMessageIntegrityLongTerm(String Username, String Realm, String Password)
        {
            // auf Username und Realm wurde bereits SASLPrep angewandt
            // deshalb hier nur Passwort
            String SASLPassword = SASLPrep.STUNSASLPrep(Password);

            // String zusammensetzten
            String concentation = Username + ":" + Realm + ":" + SASLPrep.STUNSASLPrep(Password);

            // MD5 Hash (Key) berechnen
            MD5 md5 = MD5.Create();
            Byte[] Key = md5.ComputeHash(Encoding.UTF8.GetBytes(concentation));

            // für die Wandlung des Strings in ein Byte Array sind keine Vorgaben im RFC zu finden
            // deshalb wird hier einfach UTF8 verwendet
            return CreateMessageIntegrity(Key);
        }
    }
}
