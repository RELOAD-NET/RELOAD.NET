using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public class Misc
    {
                
        public static int GetUTF8LengthFromString(String input)
        {
            byte[] dummy = Encoding.UTF8.GetBytes(input);

            return dummy.Length;
        }


        public static UInt64 GetRandomUInt64()
        {
            Byte[] randomByte = new Byte[8];
            Random random = new Random();
            
            random.NextBytes(randomByte);

            return BitConverter.ToUInt64(randomByte, 0);
        }


        public static bool CompareByteArrays(Byte[] a, Byte[] b)
        {
            // gleiche Länge?
            if (a.Length == b.Length)
            {
                return a.SequenceEqual(b);
            }

            else
                return false;
        }

    }
}
