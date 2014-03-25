using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Linq;
using System.Text;

namespace STUN
{
    public class STUNMessage
    {
        private StunMessageType m_StunMessageType;
        private UInt16 m_MessageLength;
        private Byte[] m_TransactionID = new Byte[12];


        public const UInt32 m_MagicCookie = 0x2112A442;
        private List<STUNAttribute> m_AttributeList = new List<STUNAttribute>();

        // Eigenschaften
        public StunMessageType StunMessageType { get { return m_StunMessageType; } }
        public Byte[] TransactionID { get { return m_TransactionID; } }
        public List<STUNAttribute> AttributeList { get { return m_AttributeList; } }




        // Konstruktor
        public STUNMessage(StunMessageType stunMessageType)
        {
            m_StunMessageType = stunMessageType;
            Array.Copy(Guid.NewGuid().ToByteArray(), 0, m_TransactionID, 0, 12);

        }

        public STUNMessage(StunMessageType stunMessageType, Byte[] transactionID)
        {
            m_StunMessageType = stunMessageType;
            m_TransactionID = transactionID;

        }



        public bool AddAttribute(STUNAttribute stunAttribute)
        {
            m_AttributeList.Add(stunAttribute);

            return true;
        }

        public bool Create()
        {
            // es muss nur noch die Message-Länge berechnet werden   
            m_MessageLength = 0;

            // WICHTIG: Verwendung von m_AttributeList[i].Value.Length wegen Padding
            // m_AttributeList[i].Length enthält nur die Länge der Nutzdaten
            // Hinzu kommen 4 Bytes für Attribute Type (2 Byte) und Attribute Length (2 Byte)
            for (int i = 0; i < m_AttributeList.Count; i++)
                m_MessageLength += (UInt16)(4 + m_AttributeList[i].Value.Length);


            // Überprüfung auf Message Integrity und Fingerprint Attribut
            // und falls vorhanden Werte berechnen und aktualisieren

            // sind Attribute in Message vorhanden?
            if (m_AttributeList.Count > 0)
            {
                // Überprüfe auf Message Integrity und falls vorhanden neu berechnen
                CalculateMessageIntegrity();

                // Überprüfe auf Fingerprint und falls vorhanden neu berechnen
                CalculateFingerprint();
            }

            return true;
        }

        public Byte[] ToByteArray()
        {
            int offset = 0;

            // 20 Bytes für Message Header + Message Länge
            Byte[] msg = new Byte[20 + m_MessageLength];

            // Message Type, Message Length, Magic Cookie, TransactionID
            //Byte[] msg_type = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_StunMessageType));
            Byte[] msg_type = NetworkByteArray.CreateUInt16((UInt16)m_StunMessageType);
            //Byte[] msg_length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_MessageLength));
            Byte[] msg_length = NetworkByteArray.CreateUInt16(m_MessageLength);
            //Byte[] msg_magicCookie = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(m_MagicCookie));
            Byte[] msg_magicCookie = NetworkByteArray.CreateUInt32(m_MagicCookie);

            // alles in msg Array kopieren
            Array.Copy(msg_type, 0, msg, offset, msg_type.Length);
            offset += msg_type.Length;
            Array.Copy(msg_length, 0, msg, offset, msg_length.Length);
            offset += msg_length.Length;
            Array.Copy(msg_magicCookie, 0, msg, offset, msg_magicCookie.Length);
            offset += msg_magicCookie.Length;
            Array.Copy(m_TransactionID, 0, msg, offset, m_TransactionID.Length);
            offset += m_TransactionID.Length;


            // Attribute vorhanden?
            for (int i = 0; i < m_AttributeList.Count; i++)
            {

                // Attribute Type, Attribute Length, Attribute Value
                //Byte[] att_type = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_AttributeList[i].Type));
                Byte[] att_type = NetworkByteArray.CreateUInt16((UInt16)m_AttributeList[i].Type);
                //Byte[] att_length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int16)m_AttributeList[i].Length));
                Byte[] att_length = NetworkByteArray.CreateUInt16(m_AttributeList[i].Length);

                // alles in msg Array kopieren
                Array.Copy(att_type, 0, msg, offset, att_type.Length);
                offset += att_type.Length;
                Array.Copy(att_length, 0, msg, offset, att_length.Length);
                offset += att_length.Length;
                Array.Copy(m_AttributeList[i].Value, 0, msg, offset, m_AttributeList[i].Value.Length);
                offset += m_AttributeList[i].Value.Length;

            }

            return msg;
        }

        public static STUNMessage Parse(Byte[] msg)
        {
            STUNMessage stunMessage = null;

            int offset = 0;

            // HEADER PARSEN

            // Message Type auslesen (2 Byte)
            //Int16 msg_type = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(msg, offset));
            UInt16 msg_type = NetworkByteArray.ReadUInt16(msg, offset);
            offset += 2;

            // Binding Success Response?
            if (msg_type == (UInt16)StunMessageType.BindingSuccessResponse)
                stunMessage = new STUNMessage(StunMessageType.BindingSuccessResponse);

            // Binding Error Response?
            else if (msg_type == (UInt16)StunMessageType.BindingErrorResponse)
                stunMessage = new STUNMessage(StunMessageType.BindingErrorResponse);

            // Binding Request?
            else if (msg_type == (UInt16)StunMessageType.BindingRequest)
                stunMessage = new STUNMessage(StunMessageType.BindingRequest);

            // Binding Indication?
            else if (msg_type == (UInt16)StunMessageType.BindingIndication)
                stunMessage = new STUNMessage(StunMessageType.BindingIndication);

            // Methode nicht unterstützt
            else
                return null;


            // Message Length auslesen (2 Byte)
            //UInt16 msg_length = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(msg, offset));
            UInt16 msg_length = NetworkByteArray.ReadUInt16(msg, offset);
            offset += 2;
            stunMessage.m_MessageLength = msg_length;

            // Magic Cookie auslesen (4 Byte)
            //Int32 mc = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(msg, offset));
            Int32 mc = NetworkByteArray.ReadInt32(msg, offset);
            offset += 4;

            // TransactionID auslesen (12 Byte)
            Array.Copy(msg, offset, stunMessage.m_TransactionID, 0, 12);
            offset += 12;


            // ATTRIBUTE PARSEN
            int att_bytes_left = stunMessage.m_MessageLength;

            // Attribute vorhanden?
            while (att_bytes_left > 0) // Header von 20 Bytes steht nicht in m_MessageLength
            {

                // Attribute Type auslesen (2 Byte)
                //UInt16 att_type = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(msg, offset));
                UInt16 att_type = NetworkByteArray.ReadUInt16(msg, offset);
                offset += 2;

                // Attribute Length (2 Byte)
                //UInt16 att_length = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(msg, offset));
                UInt16 att_length = NetworkByteArray.ReadUInt16(msg, offset);
                offset += 2;

                // Attribute Value auslesen (Vielfaches von 4 Byte)
                Byte[] att_value_with_padding;

                // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
                if ((att_length % 4) == 0)
                    att_value_with_padding = new Byte[att_length];

                // falls doch werden zusätzliche Bytes hinzugefügt
                else
                    att_value_with_padding = new Byte[att_length + (4 - (att_length % 4))];

                Array.Copy(msg, offset, att_value_with_padding, 0, att_value_with_padding.Length);
                offset += att_value_with_padding.Length;




                // Attribut erstellen und in Liste speichern
                //STUNAttribute attribute = new STUNAttribute();


                // Mapped Address
                if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.MappedAddress)
                {
                    MappedAddressAttribute mappedAddressAttribute = new MappedAddressAttribute();
                    mappedAddressAttribute.ParseMappedAddress(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(mappedAddressAttribute);
                }

                // Xor Mapped Address
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.XorMappedAddress)
                {
                    XorMappedAddressAttribute xorMappedAddressAttribute = new XorMappedAddressAttribute();
                    xorMappedAddressAttribute.ParseXorMappedAddress(att_length, att_value_with_padding, stunMessage.m_TransactionID);
                    stunMessage.m_AttributeList.Add(xorMappedAddressAttribute);
                }

                // Username
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Username)
                {
                    UsernameAttribute usernameAttribute = new UsernameAttribute();
                    usernameAttribute.ParseUsername(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(usernameAttribute);
                }

                // Error Code
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.ErrorCode)
                {
                    ErrorCodeAttribute errorCodeAttribute = new ErrorCodeAttribute();
                    errorCodeAttribute.ParseErrorCode(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(errorCodeAttribute);
                }

                // Unknown Attributes
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.UnknownAttributes)
                {
                    UnknownAttributesAttribute unknownAttributesAttribute = new UnknownAttributesAttribute();
                    unknownAttributesAttribute.ParseUnknownAttributes(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(unknownAttributesAttribute);
                }

                // Realm
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Realm)
                {
                    RealmAttribute realmAttribute = new RealmAttribute();
                    realmAttribute.ParseRealm(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(realmAttribute);
                }

                // Nonce
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Nonce)
                {
                    NonceAttribute nonceAttribute = new NonceAttribute();
                    nonceAttribute.ParseNonce(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(nonceAttribute);
                }

                // Software
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Software)
                {
                    SoftwareAttribute softwareAttribute = new SoftwareAttribute();
                    softwareAttribute.ParseSoftware(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(softwareAttribute);
                }

                // Alternate Server
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.AlternateServer)
                {
                    AlternateServerAttribute alternateServerAttribute = new AlternateServerAttribute();
                    alternateServerAttribute.ParseAlternateServer(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(alternateServerAttribute);
                }

                // Fingerprint
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Fingerprint)
                {
                    FingerprintAttribute fingerprintAttribute = new FingerprintAttribute();
                    fingerprintAttribute.ParseFingerprint(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(fingerprintAttribute);
                }

                // Message Integrity
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.MessageIntegrity)
                {
                    MessageIntegrityAttribute messageIntegrityAttribute = new MessageIntegrityAttribute();
                    messageIntegrityAttribute.ParseMessageIntegrity(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(messageIntegrityAttribute);
                }

                // Priority
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.Priority)
                {
                    PriorityAttribute priorityAttribute = new PriorityAttribute();
                    priorityAttribute.ParsePriority(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(priorityAttribute);
                }

                // Use Candidate
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.UseCandidate)
                {
                    // hat keinen Inhalt und dient nur als Flag, daher nichts zu parsen
                    // deshalb einfach Objekt als Flag erzeugen
                    UseCandidateAttribute useCandidateAttribute = UseCandidateAttribute.CreateUseCandidate();
                    stunMessage.m_AttributeList.Add(useCandidateAttribute);
                }

                // Ice Controlled
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.IceControlled)
                {
                    IceControlledAttribute iceControlledAttribute = new IceControlledAttribute();
                    iceControlledAttribute.ParseIceControlled(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(iceControlledAttribute);
                }

                // Ice Controlling
                else if (att_type == (UInt16)STUN.STUNAttribute.StunAttributeType.IceControlling)
                {
                    IceControllingAttribute iceControllingAttribute = new IceControllingAttribute();
                    iceControllingAttribute.ParseIceControlling(att_length, att_value_with_padding);
                    stunMessage.m_AttributeList.Add(iceControllingAttribute);
                }

                // kein bekanntes Attribut
                else
                {
                    // comprehension required?
                    if (att_type >= 0x0000 && att_type <= 0x7FFF)
                    {
                        Console.WriteLine("Unbekanntes Attribut!");
                        //stunMessage.ContainsUnknownComprehensionRequiredAttributes = true;

                    }

                    // ansonsten ignorieren
                    // tue nichts

                }

                // übrige Attribut Bytes in Message
                // 2 Byte Attribute Type + 2 Byte Attribute Length + x Byte Value (mit Padding)
                att_bytes_left -= (2 + 2 + att_value_with_padding.Length);

            }


            return stunMessage;

        }





        // vergleicht die Transaction IDs der Messages
        public bool CompareTransactionIDs(STUNMessage stunMessage)
        {
            // gleiche Länge?
            if (this.TransactionID.Length == stunMessage.TransactionID.Length)
            {
                // jedes Byte einzeln prüfen
                for (int i = 0; i < this.TransactionID.Length; i++)
                {
                    // falls Unterschied abbrechen
                    if (this.TransactionID[i] != stunMessage.TransactionID[i])
                        return false;
                }

                // falls jedes Byte gestimmt hat
                return true;
            }

            // unterschiedliche Länge
            else
                return false;

        }

        // überprüft ob das Attribut in der Message vorhanden ist
        public bool ContainsAttribute(STUNAttribute.StunAttributeType Type)
        {
            foreach (STUNAttribute attribute in this.AttributeList)
            {
                if (attribute.Type == Type)
                    return true;
            }

            return false;
        }

        // liefert den Index des Attributs falls vorhanden, andernfalls -1
        public int GetIndexOfAttribute(STUNAttribute.StunAttributeType Type)
        {
            for (int i = 0; i < this.AttributeList.Count; i++)
            {
                if (this.AttributeList[i].Type == Type)
                    return i;
            }

            return -1;
        }

        // überprüft ob unbekannte Attribute vorhanden sind
        public bool ContainsUnknownAttributes()
        {
            // wenn kein Attribut vorhanden ist, ist auch kein unbekanntes dabei
            if (m_AttributeList.Count == 0)
                return false;

            // andernfalls alle Attribute überprüfen
            else
            {
                for (int i = 0; i < m_AttributeList.Count; i++)
                {
                    // falls Attribut nicht definiert ist
                    if (!Enum.IsDefined(typeof(STUNAttribute.StunAttributeType), m_AttributeList[i].Type))
                        return true;
                }

                // alle Attribute definiert
                return false;
            }
        }

        // Berechnet und aktualisiert das Message Integrity Attribut, falls es in der Nachricht vorhanden ist
        private void CalculateMessageIntegrity()
        {
            // MESSAGE INTEGRITY
            // Zähler
            UInt16 lengthUpToMessageIntegrity = 0;
            UInt16 lengthIncludingMessageIntegrity = 0;

            // suche nach Message Integrity
            for (int i = 0; i < m_AttributeList.Count; i++)
            {
                // Längenzählung
                lengthIncludingMessageIntegrity += (UInt16)(4 + m_AttributeList[i].Value.Length);

                // gefunden
                if (m_AttributeList[i].Type == STUNAttribute.StunAttributeType.MessageIntegrity)
                {
                    // zuerst gesamte Message in Byte Array wandeln
                    Byte[] message = this.ToByteArray();

                    // ursprüngliche Länge sichern
                    UInt16 originalLength = m_MessageLength;

                    // Länge in Message überschreiben mit Länge inklusive MessageIntegrity
                    m_MessageLength = lengthIncludingMessageIntegrity;

                    // Teil der Nachricht extrahieren bis zu aber ohne MessageIntegrity
                    Byte[] HMACInput = new Byte[lengthUpToMessageIntegrity];
                    Array.Copy(message, 0, HMACInput, 0, lengthUpToMessageIntegrity);

                    // Hash berechnen und Dummy Wert überschreiben
                    MessageIntegrityAttribute messageIntegrityAttribute = (MessageIntegrityAttribute)this.m_AttributeList[i];
                    HMACSHA1 hmac = new HMACSHA1(messageIntegrityAttribute.MessageIntegrity.Key);
                    messageIntegrityAttribute.MessageIntegrity.Hash = hmac.ComputeHash(HMACInput);

                    // Value von Message Integrity Attribut aktualisieren, damit in der Funktion STUNMessage.ToByteArray() die aktuellen Daten ausgelesen werden können
                    messageIntegrityAttribute.UpdateMessageIntegrityValue();

                    // originale Message Länge wiederherstellen
                    m_MessageLength = originalLength;
                }

                // Längenzählung
                lengthUpToMessageIntegrity += (UInt16)(4 + m_AttributeList[i].Value.Length);

            }
        }

        // Validiere Message Integrity falls vorhanden
        private bool ValidateMessageIntegrity(Byte[] Key)
        {
            // Zähler
            UInt16 lengthUpToMessageIntegrity = 0;
            UInt16 lengthIncludingMessageIntegrity = 0;

            // suche nach Message Integrity
            for (int i = 0; i < m_AttributeList.Count; i++)
            {
                // Längenzählung
                lengthIncludingMessageIntegrity += (UInt16)(4 + m_AttributeList[i].Value.Length);

                // gefunden
                if (m_AttributeList[i].Type == STUNAttribute.StunAttributeType.MessageIntegrity)
                {
                    // zuerst gesamte Message in Byte Array wandeln
                    Byte[] message = this.ToByteArray();

                    // ursprüngliche Länge sichern
                    UInt16 originalLength = m_MessageLength;

                    // Länge in Message überschreiben mit Länge inklusive MessageIntegrity
                    m_MessageLength = lengthIncludingMessageIntegrity;

                    // Teil der Nachricht extrahieren bis zu aber ohne MessageIntegrity
                    Byte[] HMACInput = new Byte[lengthUpToMessageIntegrity];
                    Array.Copy(message, 0, HMACInput, 0, lengthUpToMessageIntegrity);

                    // Hash mit übergebenen Key berechnen
                    MessageIntegrityAttribute messageIntegrityAttribute = MessageIntegrityAttribute.CreateMessageIntegrity(Key);
                    HMACSHA1 hmac = new HMACSHA1(messageIntegrityAttribute.MessageIntegrity.Key);
                    messageIntegrityAttribute.MessageIntegrity.Hash = hmac.ComputeHash(HMACInput);

                    // originale Message Länge wiederherstellen
                    m_MessageLength = originalLength;

                    // überprüfen ob Hashs identisch sind

                    return Misc.CompareByteArrays(
                        ((MessageIntegrityAttribute)this.m_AttributeList[i]).MessageIntegrity.Hash,
                        messageIntegrityAttribute.MessageIntegrity.Hash);
                }

                // Längenzählung
                lengthUpToMessageIntegrity += (UInt16)(4 + m_AttributeList[i].Value.Length);
            }

            // kein Message Integrity Attribut gefunden
            return false;
        }

        // Validiere Message Integrity Short Term falls vorhanden
        public bool ValidateMessageIntegrityShortTerm(String Password)
        {
            String SASLPassword = SASLPrep.STUNSASLPrep(Password);

            // für die Wandlung des Strings in ein Byte Array sind keine Vorgaben im RFC zu finden
            // deshalb wird hier einfach UTF8 verwendet
            return ValidateMessageIntegrity(Encoding.UTF8.GetBytes(SASLPassword));
        }

        // Validiere Message Integrity Long Term falls vorhanden
        public bool ValidateMessageIntegrityLongTerm(String Username, String Realm, String Password)
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
            return ValidateMessageIntegrity(Key);
        }

        // Berechnet und aktualisiert das Fingerprint Attribut, falls es in der Nachricht vorhanden ist
        private void CalculateFingerprint()
        {
            // FINGERPRINT
            // wenn letztes Attribut FINGERPRINT Attribut ist => CRC berechnen und Value(initialer Dummy Wert) ändern
            if (this.m_AttributeList[m_AttributeList.Count - 1].Type == STUNAttribute.StunAttributeType.Fingerprint)
            {
                // zuerst gesamte Message in Byte Array wandeln
                Byte[] msg_with_fingerprint = this.ToByteArray();

                // dann Fingerprint Attribut entfernen um CRC berechnen zu können
                Byte[] msg_without_fingerprint = new Byte[msg_with_fingerprint.Length - 8];                         // Fingerprint ist 8 Byte groß (2 Byte Type, 2 Byte Length, 4 Byte Value)
                Array.Copy(msg_with_fingerprint, 0, msg_without_fingerprint, 0, msg_without_fingerprint.Length);

                // jetzt CRC von msg_without_fingerprint berechnen
                UInt32 CRC = CRC32.CalculateCRC32Checksum(msg_without_fingerprint);

                // CRC Xoren mit 0x5354554E
                UInt32 fingerprint = CRC ^ 0x5354554E;

                // Ergebnis in Fingerprint Attribut kopieren
                FingerprintAttribute fingerprintAttribute = (FingerprintAttribute)this.m_AttributeList[m_AttributeList.Count - 1];
                fingerprintAttribute.Fingerprint = fingerprint;

                // Value von Fingerprint Attribut aktualisieren, damit in der Funktion STUNMessage.ToByteArray() die aktuellen Daten ausgelesen werden können
                fingerprintAttribute.UpdateFingerprintValue();

            }
        }

        // überprüft ob ein Fingerprint Attribut vorhanden ist
        public bool ContainsFingerprint()
        {
            // Attribute vorhanden?
            if (this.m_AttributeList.Count > 0)
            {
                // Fingerprint Attribut muss letztes Attribut sein
                if (this.m_AttributeList[m_AttributeList.Count - 1].Type == STUNAttribute.StunAttributeType.Fingerprint)
                    return true;
                else
                    return false;
            }
            else
                return false;
        }

        // Validiere Fingerprint (erfordert vorherige Überprüfung mit ContainsFingerprint() !!!)
        public bool ValidateFingerprint()
        {
            // CRC berechnen und Value(initialer Dummy Wert) ändern

            // zuerst gesamte Message in Byte Array wandeln
            Byte[] msg_with_fingerprint = this.ToByteArray();

            // dann Fingerprint Attribut entfernen um CRC berechnen zu können
            Byte[] msg_without_fingerprint = new Byte[msg_with_fingerprint.Length - 8];                         // Fingerprint ist 8 Byte groß (2 Byte Type, 2 Byte Length, 4 Byte Value)
            Array.Copy(msg_with_fingerprint, 0, msg_without_fingerprint, 0, msg_without_fingerprint.Length);

            // jetzt CRC von msg_without_fingerprint berechnen
            UInt32 CRC = CRC32.CalculateCRC32Checksum(msg_without_fingerprint);

            // CRC Xoren mit 0x5354554E
            UInt32 fingerprint = CRC ^ 0x5354554E;

            // Ergebnis vergleichen
            if (((FingerprintAttribute)this.m_AttributeList[m_AttributeList.Count - 1]).Fingerprint == fingerprint)
                return true;
            else
                return false;
            
        }


    }
}
