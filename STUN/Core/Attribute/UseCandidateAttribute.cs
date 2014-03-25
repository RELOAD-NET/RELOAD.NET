using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class UseCandidateAttribute : STUNAttribute
    {
        // Attribut dient als Flag und hat keinen Inhalt (s. ICE RFC 5245 Section 19.1)

        public static UseCandidateAttribute CreateUseCandidate()
        {
            UseCandidateAttribute useCandidateAttribute = new UseCandidateAttribute();

            useCandidateAttribute.m_Type = StunAttributeType.UseCandidate;
            useCandidateAttribute.m_Value = new Byte[]{};   // hat keinen Inhalt
            useCandidateAttribute.m_Length = 0;             // hat keinen Inhalt und daher die Länge 0

            return useCandidateAttribute;
        }
               
    }
}
