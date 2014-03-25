using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TSystems.RELOAD.Utils
{
    public class NetworkByteArray
    {
        #region Read Methoden

        public static UInt16 ReadUInt16(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] uint16 = new Byte[2];
            Array.Copy(byteArray, startIndex, uint16, 0, 2);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(uint16);

            // Umwandlung und Rückgabe
            return BitConverter.ToUInt16(uint16, 0);
        }


        public static UInt32 ReadUInt32(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] uint32 = new Byte[4];
            Array.Copy(byteArray, startIndex, uint32, 0, 4);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(uint32);

            // Umwandlung und Rückgabe
            return BitConverter.ToUInt32(uint32, 0);
        }


        public static UInt64 ReadUInt64(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] uint64 = new Byte[8];
            Array.Copy(byteArray, startIndex, uint64, 0, 8);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(uint64);

            // Umwandlung und Rückgabe
            return BitConverter.ToUInt64(uint64, 0);
        }


        public static Int16 ReadInt16(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] int16 = new Byte[2];
            Array.Copy(byteArray, startIndex, int16, 0, 2);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(int16);

            // Umwandlung und Rückgabe
            return BitConverter.ToInt16(int16, 0);
        }


        public static Int32 ReadInt32(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] int32 = new Byte[4];
            Array.Copy(byteArray, startIndex, int32, 0, 4);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(int32);

            // Umwandlung und Rückgabe
            return BitConverter.ToInt32(int32, 0);
        }


        public static Int64 ReadInt64(Byte[] byteArray, int startIndex)
        {
            // Bytes auslesen
            Byte[] int64 = new Byte[8];
            Array.Copy(byteArray, startIndex, int64, 0, 8);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(int64);

            // Umwandlung und Rückgabe
            return BitConverter.ToInt64(int64, 0);
        }

        #endregion

        #region Create Methoden

        public static Byte[] CreateUInt16(UInt16 uint16)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(uint16);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }


        public static Byte[] CreateInt16(Int16 int16)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(int16);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }


        public static Byte[] CreateUInt32(UInt32 uint32)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(uint32);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }


        public static Byte[] CreateInt32(Int32 int32)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(int32);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }


        public static Byte[] CreateUInt64(UInt64 uint64)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(uint64);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }


        public static Byte[] CreateInt64(Int64 int64)
        {
            // in Byte Array wandeln
            Byte[] byteArray = BitConverter.GetBytes(int64);

            // wenn System Little Endian ist muss gewandelt werden
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteArray);

            // Array zurückgeben
            return byteArray;
        }

        #endregion
                
    }
}
