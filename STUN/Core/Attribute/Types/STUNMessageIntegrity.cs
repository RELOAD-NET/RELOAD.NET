using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class STUNMessageIntegrity
    {
        private Byte[] m_Hash;
        private Byte[] m_Key;

        public Byte[] Hash { get { return m_Hash; } set { m_Hash = value; } }
        public Byte[] Key { get { return m_Key; } }

        public STUNMessageIntegrity()
        {
            m_Hash = new Byte[20];  // fixe Länge
        }

        public STUNMessageIntegrity(Byte[] Key)
        {
            m_Hash = new Byte[20];  // fixe Länge
            m_Key = Key;
        }
    }
}
