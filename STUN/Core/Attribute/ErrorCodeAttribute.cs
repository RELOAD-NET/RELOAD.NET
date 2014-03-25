using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class ErrorCodeAttribute : STUNAttribute
    {
        public enum StunErrorType
        {
            // STUN:
            TryAlternate = 300,
            BadRequest = 400,
            Unauthorized = 401,
            UnknownAttribute = 420,
            StaleNonce = 438,
            ServerError = 500,

            // ICE:
            RoleConflict = 487
        }

        //private List<KeyValuePair<ushort, string>> StunErrorCodeList = new List<KeyValuePair<ushort, string>>
        //{
        //    new KeyValuePair<ushort,string>(300, "Try Alternate"),
        //    new KeyValuePair<ushort,string>(400, "Bad Request"),                  
        //};

        private STUNErrorCode m_ErrorCode = null;

        public STUNErrorCode ErrorCode { get { return m_ErrorCode; } }

        public static ErrorCodeAttribute CreareErrorCode(StunErrorType Type)
        {
            return CreateErrorCode((UInt16)Type, Type.ToString());
        }

        public static ErrorCodeAttribute CreateErrorCode(int ErrorCode, String ReasonPhrase)
        {
            ErrorCodeAttribute errorCodeAttribute = new ErrorCodeAttribute();

            errorCodeAttribute.m_ErrorCode = new STUNErrorCode(ErrorCode, ReasonPhrase);

            errorCodeAttribute.m_Type = StunAttributeType.ErrorCode;

            //stunAttribute.m_Value = stunAttribute.ErrorCodeAttributeToByteArray();       //=>>>>>>>>>>>>>>>>>>>>>>>> Falsche funktion, liefert nicht nur value sondern gesamtes Attribut als Array
            errorCodeAttribute.m_Value = STUNErrorCode.ToByteArray(errorCodeAttribute.m_ErrorCode);

            // 4 Byte für Reserved, Class und Number + Reason Phrase
            //stunAttribute.m_Length = (Int16)(4 + ReasonPhrase.Length);    // FALSCH, wegen UTF8 Kodierung !!!!
            errorCodeAttribute.m_Length = (UInt16)(4 + Misc.GetUTF8LengthFromString(errorCodeAttribute.m_ErrorCode.ReasonPhrase));


            return errorCodeAttribute;

        }

        public void ParseErrorCode(UInt16 att_length, Byte[] att_value_with_padding)
        {
            m_Type = StunAttributeType.ErrorCode;
            m_Length = att_length;
            m_Value = att_value_with_padding;

            // Padding entfernen
            Byte[] error_code = new Byte[att_length];
            Array.Copy(att_value_with_padding, 0, error_code, 0, att_length);

            m_ErrorCode = STUNErrorCode.Parse(error_code);

        }
    }
}
