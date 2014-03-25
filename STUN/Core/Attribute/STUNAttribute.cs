using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace STUN
{
    public class STUNAttribute
    {
        // ENUMS
        public enum StunAttributeType
        {
            // comprehension-required range (0x0000 - 0x7FFF):
            // STUN:
            MappedAddress = 0x0001,
            Username = 0x0006,
            MessageIntegrity = 0x0008,
            ErrorCode = 0x0009,
            UnknownAttributes = 0x000A,
            Realm = 0x0014,
            Nonce = 0x0015,
            XorMappedAddress = 0x0020,

            // ICE:
            Priority = 0x0024,
            UseCandidate = 0x0025,


            // comprehension-optional range (0x8000 - 0xFFFF):
            // STUN:
            Software = 0x8022,
            AlternateServer = 0x8023,
            Fingerprint = 0x8028,

            // ICE:
            IceControlled = 0x8029,
            IceControlling = 0x802A
        }

        protected enum AddressFamily
        {
            IPv4 = 0x01,
            IPv6 = 0x02
        }



        // allgemeine Attribut Daten
        protected StunAttributeType m_Type;
        protected UInt16 m_Length;
        protected Byte[] m_Value;


       
        public StunAttributeType Type { get { return m_Type; } }
        public UInt16 Length { get { return m_Length; } }
        public Byte[] Value { get { return m_Value; } }

        
        

       

    }
}
