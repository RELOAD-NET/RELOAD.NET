using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class FingerprintAttribute : STUNAttribute
    {
        private UInt32 m_Fingerprint = 0;

        public UInt32 Fingerprint { get { return m_Fingerprint; } set { m_Fingerprint = value; } }

        public static FingerprintAttribute CreateFingerprint()
        {
            FingerprintAttribute fingerprintAttribute = new FingerprintAttribute();

            // Dummy Wert (siehe RFC 5389 Section 15.5)
            fingerprintAttribute.m_Fingerprint = 0;

            fingerprintAttribute.m_Type = StunAttributeType.Fingerprint;
            fingerprintAttribute.m_Value = new Byte[4];                            // initial Dummy, Länge fest: 4 Byte (UInt32)
            fingerprintAttribute.m_Length = (UInt16)fingerprintAttribute.m_Value.Length;   // m_Value.Length passt hier, weil m_Value 4 Bytes groß ist


            return fingerprintAttribute;
        }

        private Byte[] FingerprintToByteArray()
        {
            // 4 Byte Value, passt
            //return BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)m_Fingerprint));
            return NetworkByteArray.CreateUInt32(m_Fingerprint);
        }

        public void ParseFingerprint(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.Fingerprint;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            //m_Fingerprint = (UInt32)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(m_Value, 0));
            m_Fingerprint = NetworkByteArray.ReadUInt32(m_Value, 0);
        }

        public void UpdateFingerprintValue()
        {
            //m_Value = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)m_Fingerprint));
            m_Value = NetworkByteArray.CreateUInt32(m_Fingerprint);
        }
    }
}
