using System;
using System.Net;
using System.Text;

namespace STUN
{
    public class STUNErrorCode
    {
        private int m_ErrorCode;
        private String m_ReasonPhrase;


        // Eigenschaften
        public int ErrorCode { get { return m_ErrorCode; } }
        public String ReasonPhrase { get { return m_ReasonPhrase; } }


        // Konstruktor
        public STUNErrorCode(int ErrorCode, String ReasonPhrase)
        {
            m_ErrorCode = ErrorCode;
            m_ReasonPhrase = ReasonPhrase;
        }


        // Funktionen
        public static Byte[] ToByteArray(STUNErrorCode stunErrorCode)
        {
            // 2 Byte Nullen + 2 Byte Class und Number + Reason Phrase (falls nötig auf Vielfaches von 4 Byte erhöht)
            Byte[] errorCode;
            int reasonPhraseUTF8length = Misc.GetUTF8LengthFromString(stunErrorCode.m_ReasonPhrase);

            // falls Vielfaches von 4 Byte keine zusätzlichen Bytes
            if ((reasonPhraseUTF8length % 4) == 0)
                errorCode = new Byte[4 + reasonPhraseUTF8length];

            // falls nicht werden zusätzliche Bytes hinzugefügt
            else
                errorCode = new Byte[4 + reasonPhraseUTF8length + (4 - (reasonPhraseUTF8length % 4))];


            int offset = 0;

            // ersten 2 Byte = 0
            errorCode[offset++] = 0;
            errorCode[offset++] = 0;

            //Byte klasse = (Byte)((stunErrorCode.m_ErrorCode / 100) << 8); // FEHLER
            Byte klasse = (Byte)(stunErrorCode.m_ErrorCode / 100);
            Byte number = (Byte)(stunErrorCode.m_ErrorCode % 100);

            // nächsten 2 Byte Class und Number 
            errorCode[offset++] = klasse;
            errorCode[offset++] = number;

            // Reason Phrase Byte Array anlegen (UTF-8)
            Byte[] reason_phrase = Encoding.UTF8.GetBytes(stunErrorCode.m_ReasonPhrase);


            // reason_phrase in errorCode kopieren
            Array.Copy(reason_phrase, 0, errorCode, offset, reasonPhraseUTF8length);

            return errorCode;
        }


        public static STUNErrorCode Parse(Byte[] errorCode)
        {

            int offset = 0;

            // die ersten 2 Byte skippen da alles Nullen
            offset += 2;

            // nächsten 2 Byte enthalten Class und Number
            //UInt16 classAndNumber = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(errorCode, offset));
            UInt16 classAndNumber = NetworkByteArray.ReadUInt16(errorCode, offset);
            offset += 2;

            // Class extrahieren (zwischen 3 und 6)
            // zuerst die relevanten Bits verUNDen und dann den Stellenwert ändern (entweder durch 100 teilen, oder 8 nach rechts shiften)
            UInt16 klasse = (UInt16)((classAndNumber & 0x500) >> 8);

            // Number extrahieren (zwischen 0 und 99)
            UInt16 number = (UInt16)(classAndNumber & 0xFF);


            // Error Code zusammensetzten
            int ErrorCode = (klasse * 100) + number;

            // Reason Phrase
            String ReasonPhrase = Encoding.UTF8.GetString(errorCode, offset, errorCode.Length - 4);

            STUNErrorCode stunErrorCode = new STUNErrorCode(ErrorCode, ReasonPhrase);

            return stunErrorCode;
        }
    }
}
