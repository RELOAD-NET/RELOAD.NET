/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012, Telekom Deutschland GmbH 
*
* This file is part of RELOAD.NET.
*
* RELOAD.NET is free software: you can redistribute it and/or modify
* it under the terms of the GNU Lesser General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* RELOAD.NET is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU Lesser General Public License for more details.
*
* You should have received a copy of the GNU Lesser General Public License
* along with RELOAD.NET.  If not, see <http://www.gnu.org/licenses/>.
*
* see https://github.com/RELOAD-NET/RELOAD.NET
* 
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Security.Cryptography;
#pragma warning disable 660, 661

namespace TSystems.RELOAD.Utils {

    public class BigInt : IComparable
    {
        private byte[] m_data;

        public byte[] Data {
            get { return m_data; }
            set { m_data = value; }
        }

        private int m_digits = 20;
        public int Digits {
            get { return m_digits; }
            set { m_digits = value; }
        }

        public BigInt() {
            this.m_data = new byte[this.m_digits];
        }
        public BigInt(byte[] data) {
            //TK removed, + operator will throw this on addition of short length ResourceID + 1
            //System.Diagnostics.Debug.Assert(data == null || data.Length == m_digits);
            this.m_data = data;
            this.m_digits = data.Length;
        }

        public BigInt Max() {
            BigInt b = new BigInt();
            for (int i = 0; i < m_digits; i++)
                b.m_data[i] = 0xFF;
            return b;
        }
        public BigInt Min() {
            BigInt b = new BigInt();
            for (int i = 0; i < m_digits; i++)
                b.m_data[i] = 0x00;
            return b;
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator +(BigInt a, BigInt b) {
            if (((object)a == null) || ((object)b == null))
                return a;
            int carry = 0;
            byte[] result = new byte[a.m_digits];
            for (int i = a.m_digits - 1; i >= 0; i--) {
                int c = a.m_data[i] + b.m_data[i] + carry;
                carry = c >> 8;
                result[i] = (byte)(c);
            }

            return new BigInt(result);
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator +(BigInt a, byte b) {
            if (((object)a == null) || (b == 0))
                return a;
            int carry = 0;
            byte[] result = new byte[a.m_digits];

            bool first = true;

            for (int i = a.m_digits - 1; i >= 0; i--) {
                int c = first ? (a.m_data[i] + b) : (a.m_data[i] + carry);
                first = false;
                carry = c >> 8;
                result[i] = (byte)(c);
            }
            return new BigInt(result);
        }


        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return true;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            if (a.m_data.Length != b.m_data.Length) {
                return false;
            }
            for (int i = a.m_digits - 1; i >= 0; i--) {
                if (a.m_data[i] != b.m_data[i])
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(BigInt a, BigInt b) {
            return !(a == b);
        }

        /// <summary>
        /// Implements the operator <.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator <(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return false;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            for (int i = 0; i < a.m_digits; i++) {
                int delta = a.m_data[i] - b.m_data[i];
                if (delta != 0) {
                    return delta < 0;
                }
            }
            return false;
        }

        /// <summary>
        /// Implements the operator >.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator >(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return false;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            for (int i = 0; i < a.m_digits; i++) {
                int delta = a.m_data[i] - b.m_data[i];
                if (delta != 0) {
                    return delta > 0;
                }
            }
            return false;
        }


        /// <summary>
        /// Implements the operator <=.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator <=(BigInt a, BigInt b) {
            return (a < b) || (a == b);
        }

        /// <summary>
        /// Implements the operator >=
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator >=(BigInt a, BigInt b) {
            return (a > b) || (a == b);
        }

        /// <summary>
        /// Implements the operator <<, 
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="shift">The shift.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator <<(BigInt a, int shift) {
            if (((object)a == null) || shift == 0) {
                return a;
            }

            byte[] result = new byte[a.m_digits];
            Array.Copy(a.m_data, result, a.m_digits);
            int carry = 0;
            for (int j = 0; j < shift; j++) {
                for (int i = a.m_digits - 1; i >= 0; i--) {
                    int newcarry = (result[i] & 0x80) != 0 ? 1 : 0;
                    result[i] = (byte)((byte)result[i] + (byte)result[i] + (byte)carry);
                    carry = newcarry;
                }
            }
            return new BigInt(result);
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString() {
            if (m_data == null)
                return "<null>";
            else {
                string s = "";
                for (int i = 0; i < m_data.Length; i++) {
                    s += String.Format("{0:X2}", m_data[i]);
                }
                return s;
            }
        }

       /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>; otherwise, false.
        /// </returns>
        /// <exception cref="T:System.NullReferenceException">
        /// The <paramref name="obj"/> parameter is null.
        /// </exception>
        public override bool Equals(object obj) {
            if (System.Object.ReferenceEquals(this, obj)) {
                return true;
            }
            if (((object)this == null) || ((object)obj == null)) {
                return false;
            }
            for (int i = this.m_digits - 1; i >= 0; i--) {
                if (this.m_data[i] != ((BigInt)obj).m_data[i])
                    return false;
            }
            return true;
        }


        public override int GetHashCode()
        {
           //return ((object)this).GetHashCode();
           return this.ToString().GetHashCode();
        }

        public int CompareTo(Object o)
        {
            if (o is BigInt)
            {
                if ((BigInt)o > this)
                {
                    return -1;
                }
                else if (this > (BigInt)o)
                {
                    return 1;
                }
                else if (this == (BigInt)o)
                {
                    return 0;
                }
            }
            return 0;
        }
    
    }

    public class NodeId : BigInt {
        public NodeId() {
            this.Digits = ReloadGlobals.NODE_ID_DIGITS;
            this.Data = new byte[this.Digits];
        }
        public NodeId(byte[] a) {
            this.Digits = ReloadGlobals.NODE_ID_DIGITS;
            this.Data = new byte[this.Digits];

            if (a == null)
                System.Diagnostics.Debug.Assert(a == null);
            else{
                Array.Clear(this.Data, 0, this.Data.Length);
                if (this.Data.Length > a.Length)
                    Array.Copy(a, 0, this.Data, this.Data.Length - a.Length, a.Length);
                else
                    Array.Copy(a, this.Data, this.Data.Length);
            }
        }
        public NodeId(ResourceId resourceid)
              // call constructor above
            : this()
        {

            if (resourceid == null)
                System.Diagnostics.Debug.Assert(resourceid == null);
            else{
                //the following function fills the array with zeros, the intention is to have leading zero padding bytes if resourceid is smaller
                Array.Clear(this.Data, 0, this.Data.Length);
                if (this.Data.Length > resourceid.Data.Length)
                    Array.Copy(resourceid.Data, 0, this.Data, this.Data.Length - resourceid.Data.Length, resourceid.Data.Length);
                else
                    Array.Copy(resourceid.Data, this.Data, this.Data.Length);
            }
        }
        public static NodeId operator +(NodeId a, NodeId b)
        {
            if (((object)a == null) || ((object)b == null))
                return a;
            int carry = 0;
            byte[] result = new byte[a.Digits];
            for (int i = a.Digits - 1; i >= 0; i--)
            {
                int c = a.Data[i] + b.Data[i] + carry;
                carry = c >> 8;
                result[i] = (byte)(c);
            }

            return new NodeId(result);
        }

        //public static bool operator ==(NodeId a, NodeId b)
        //{
        //    if (System.Object.ReferenceEquals(a, b)) {
        //        return true;
        //    }
        //    if (((object)a == null) || ((object)b == null)) {
        //        return false;
        //    }
        //    if(a.Data.Length != b.Data.Length) {
        //        return false;
        //    }

        //    for (int i = a.Data.Length - 1; i >= 0; i--)
        //    {
        //        if (a.Data[i] != b.Data[i])
        //            return false;
        //    }
        //    return true;
        //}

        /// <summary>
        /// Implements the operator <<, 
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="shift">The shift.</param>
        /// <returns>The result of the operator.</returns>
        public static NodeId operator <<(NodeId a, int shift)
        {
            if (((object)a == null) || shift == 0)
            {
                return a;
            }

            byte[] result = new byte[a.Digits];
            Array.Copy(a.Data, result, a.Digits);
            int carry = 0;
            for (int j = 0; j < shift; j++)
            {
                for (int i = a.Digits - 1; i >= 0; i--)
                {
                    int newcarry = (result[i] & 0x80) != 0 ? 1 : 0;
                    result[i] = (byte)((byte)result[i] + (byte)result[i] + (byte)carry);
                    carry = newcarry;
                }
            }
            return new NodeId(result);
        }

		/// <summary>
        /// Implements the operator >>, 
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="shift">The shift.</param>
        /// <returns>The result of the operator.</returns>
        public static NodeId operator >>(NodeId a, int shift)
        {
            if (((object)a == null) || shift == 0)
            {
                return a;
            }
            System.Collections.BitArray input = new System.Collections.BitArray(a.Data);
            System.Collections.BitArray resultarray = new System.Collections.BitArray(a.Digits * 8);

            byte[] result = new byte[a.Digits];

            for (int i = a.Digits * 8 - 1; i >= a.Digits * 8 - 1 - shift; i--)
            {
                resultarray[i] = false;
            }

            for (int i = a.Digits * 8 - 1 - shift; i >= 0; i--)
            {
                resultarray.Set(i, input.Get(i + shift));
            }

            resultarray.CopyTo(result, 0);

            Array.Reverse(result);

            return new NodeId(result);
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static NodeId operator +(NodeId a, byte b)
        {
            if (((object)a == null) || (b == 0))
                return a;
            int carry = 0;
            byte[] result = new byte[a.Digits];

            bool first = true;

            for (int i = a.Digits - 1; i >= 0; i--)
            {
                int c = first ? (a.Data[i] + b) : (a.Data[i] + carry);
                first = false;
                carry = c >> 8;
                result[i] = (byte)(c);
            }
            return new NodeId(result);
        }

        new public NodeId Max()
        {
            NodeId b = new NodeId();
            for (int i = 0; i < b.Digits; i++)
                b.Data[i] = 0xFF;
            return b;
        }

        new public NodeId Min()
        {
            NodeId b = new NodeId();
            for (int i = 0; i < b.Digits; i++)
                b.Data[i] = 0x00;
            return b;
        }

    }

    public class ResourceId : BigInt {
        public ResourceId(byte[] a) {

            int iFirstNoneZero = 0;

            if (a == null)
                System.Diagnostics.Debug.Assert(a == null);
 
            foreach (byte b in a){
                if (b != 0)
                    break;
                ++iFirstNoneZero;
            }

            this.Digits = a.Length - iFirstNoneZero;
            this.Data = new byte[this.Digits];
            Array.Copy(a, iFirstNoneZero, this.Data, 0, this.Data.Length);
        }

        public ResourceId(NodeId nodeid)
            // call constructor above
            : this(nodeid.Data){
        }

        public ResourceId(BigInt bigint)
            // call constructor above
            : this(bigint.Data)
        {
        }

        public ResourceId(string str)
        {
            SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider();
            byte[] buffer = sha1.ComputeHash(new System.Text.ASCIIEncoding().GetBytes(str));

            this.Digits = 16;
            this.Data = new byte[this.Digits];

            if (buffer.Length > 16)
                Array.Copy(buffer, buffer.Length - 16, this.Data, 0, 16);
            else
                this.Data = buffer;
        }

        public static ResourceId operator +(ResourceId a, ResourceId b)
        {
            if (((object)a == null) || ((object)b == null))
                return a;
            int carry = 0;
            byte[] result = new byte[a.Digits];
            for (int i = a.Digits - 1; i >= 0; i--)
            {
                int c = a.Data[i] + b.Data[i] + carry;
                carry = c >> 8;
                result[i] = (byte)(c);
            }

            return new ResourceId(result);
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static ResourceId operator +(ResourceId a, byte b)
        {
            if (((object)a == null) || (b == 0))
                return a;
            int carry = 0;
            byte[] result = new byte[a.Digits];

            bool first = true;

            for (int i = a.Digits - 1; i >= 0; i--)
            {
                int c = first ? (a.Data[i] + b) : (a.Data[i] + carry);
                first = false;
                carry = c >> 8;
                result[i] = (byte)(c);
            }
            return new ResourceId(result);
        }
    
    }



#if false
    public class NodeId: BigInt{

        private static int m_digits = ReloadGlobals.MAX_NODE_ID_DIGITS;

        public byte[] m_data = new byte[m_digits];

        public NodeId(byte[] data)
        {
            System.Diagnostics.Debug.Assert(data == null || data.Length == m_digits);
            this.m_data = data;
        }
        public NodeId (ResourceId resourceid)
        {
            if (resourceid == null)
            {
                System.Diagnostics.Debug.Assert(resourceid == null);
            }
            else
            {
                for (int i = 0; i < resourceid.Digits; i++)
                    m_data[i] = resourceid.m_data[i];
            }
        }
    }

    public class ResourceId : BigInt{

        private static int m_digits = ReloadGlobals.MAX_RESOURCE_ID_DIGITS;

        public byte[] m_data = new byte[m_digits];

        public ResourceId(byte[] data)
        {
            System.Diagnostics.Debug.Assert(data == null || data.Length == m_digits);
            this.m_data = data;
        }

        public ResourceId(NodeId nodeid){
            if (nodeid == null)
            {
                System.Diagnostics.Debug.Assert(nodeid == null);
            }
            else
            {
                for (int i=0; i < nodeid.Digits; i++)
                    m_data[i] = nodeid.m_data[i];
            }
        }
    }

#endif

#if false
    public class BigInt {
        private static int m_digits = 16;

        public byte[] m_data = new byte[m_digits];

        public int Digits{
            get { return m_digits; }
        }

        public BigInt() {
        }
        public BigInt(byte[] data) {
            System.Diagnostics.Debug.Assert(data == null || data.Length == m_digits);
            this.m_data = data;
        }

        public BigInt Max() {
            BigInt b = new BigInt();
            for (int i = 0; i < m_digits; i++)
                b.m_data[i] = 0xFF;
            return b;
        }
        public BigInt Min() {
            BigInt b = new BigInt();
            for (int i = 0; i < m_digits; i++)
                b.m_data[i] = 0x00;
            return b;
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator +(BigInt a, BigInt b) {
            if (((object)a == null) || ((object)b == null))
                return a;
            int carry = 0;
            byte[] result = new byte[m_digits];
            for (int i = m_digits - 1; i >= 0; i--)
            {
                int c = a.m_data[i] + b.m_data[i] + carry;
                carry = c >> 8;
                result[i] = (byte)(c);
            }
            return new BigInt(result);
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator +(BigInt a, byte b)
        {
            if (((object)a == null) || (b == 0))
                return a;
            int carry = 0;
            byte[] result = new byte[m_digits];

            bool first = true;
            
            for (int i = m_digits - 1; i >= 0; i--)
            {
                int c = first ? (a.m_data[i] + b) : (a.m_data[i] + carry);
                first = false;
                carry = c >> 8;
                result[i] = (byte)(c);
            }
            return new BigInt(result);
        }

        
        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return true;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            for (int i = m_digits - 1; i >= 0; i--)
            {
                if (a.m_data[i] != b.m_data[i])
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(BigInt a, BigInt b) {
            return !(a == b);
        }

        /// <summary>
        /// Implements the operator <.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator <(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return false;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            for (int i = 0; i < m_digits; i++)
            {
                int delta = a.m_data[i] - b.m_data[i];
                if (delta != 0) {
                    return delta < 0;
                }
            }
            return false;
        }

        /// <summary>
        /// Implements the operator >.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator >(BigInt a, BigInt b) {
            if (System.Object.ReferenceEquals(a, b)) {
                return false;
            }
            if (((object)a == null) || ((object)b == null)) {
                return false;
            }
            for (int i = 0; i < m_digits; i++) {
                int delta = a.m_data[i] - b.m_data[i];
                if (delta != 0) {
                    return delta > 0;
                }
            }
            return false;
        }


        /// <summary>
        /// Implements the operator <=.
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator <=(BigInt a, BigInt b) {
            return (a < b) || (a == b);
        }

        /// <summary>
        /// Implements the operator >=
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="b">The b.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator >=(BigInt a, BigInt b) {
            return (a > b) || (a == b);
        }

        /// <summary>
        /// Implements the operator <<, 
        /// </summary>
        /// <param name="a">A.</param>
        /// <param name="shift">The shift.</param>
        /// <returns>The result of the operator.</returns>
        public static BigInt operator <<(BigInt a, int shift) {
            if (((object)a == null) || shift == 0) {
                return a;
            }

            byte[] result = new byte[m_digits];
            Array.Copy(a.m_data, result, m_digits);
            int carry = 0;
            for (int j = 0; j < shift; j++) {
                for (int i = m_digits - 1; i >= 0; i--)
                {
                    int newcarry = (result[i] & 0x80) != 0 ? 1 : 0;
                    result[i] = (byte)((byte)result[i] + (byte)result[i] + (byte)carry);
                    carry = newcarry;
                }
            }
            return new BigInt(result);
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString() {
            if (m_data == null)
                return "<null>";
            else {
                string s = "";
                for (int i = 0; i < m_data.Length; i++) {
                    s += String.Format("{0:X2}", m_data[i]);
                }
                return s;
            }
        }
	}

#endif
    /// <summary>
    /// Extends BigInt data type for convenience
    /// </summary>
    public static class BigIntExtensions
    {
        public static bool ElementOfInterval(this BigInt value, BigInt start, BigInt end, bool endIncluded)
        {
            if (endIncluded && value == end)
            {
                return true;
            }
            else
            {
                if (start == end)
                {
                    return true;
                }
                else
                {
                    if (start > end)
                    {
                        if (value > start || value < end)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (value > start && value < end)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
    }
}
