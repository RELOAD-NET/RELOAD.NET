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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace TSystems.RELOAD.Utils {

  public static class DateTime2 {
    private static int m_offset = 0;

    static DateTime2() {
      int s = DateTime.Now.Second;
      while (true) {
        int s2 = DateTime.Now.Second;

        // wait for a rollover
        if (s != s2) {
          m_offset = Environment.TickCount % 1000;
          break;
        }
      }
    }

    public static DateTime Now {
      get {
        // find where we are based on the os tick
        int tick = Environment.TickCount % 1000;

        // calculate our ms shift from our base m_offset
        int ms = (tick >= m_offset) ? (tick - m_offset) : (1000 - (m_offset - tick));

        // build a new DateTime with our calculated ms
        // we use a new DateTime because some devices fill ms with a non-zero garbage value
        DateTime now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Month, now.Second, ms);
      }
    }

    public static void Calibrate(int seconds) {
      int s = DateTime2.Now.Second;
      int sum = 0;
      int remaining = seconds;
      while (remaining > 0) {
        DateTime dt = DateTime2.Now;
        int s2 = dt.Second;
        if (s != s2) {
          System.Diagnostics.Debug.WriteLine("ms=" + dt.Millisecond);
          remaining--;
          // store the offset from zero
          sum += (dt.Millisecond > 500) ? (dt.Millisecond - 1000) : dt.Millisecond;
          s = dt.Second;
        }
      }

      // adjust the offset by the average deviation from zero (round to the integer farthest from zero)
      if (sum < 0) {
        m_offset += (int)Math.Floor(sum / (float)seconds);
      }
      else {
        m_offset += (int)Math.Ceiling(sum / (float)seconds);
      }
    }
  }

  public class StreamUtil {

    /// <summary>
    /// This util writes the amount of written bytes at the positionBeforeWrite 
    /// arg. Use is exactly at the position were you completed writing!
    /// </summary>
    /// <param name="posBeforeWrite">long- position before writing</param>
    /// <param name="writer">The binary writer used to write</param>
    public static UInt32 WrittenBytes(long posBeforeWrite,
      BinaryWriter writer) {
      long posAfterWrite = writer.BaseStream.Position;
      long writtenBytes = posAfterWrite - posBeforeWrite;
      writer.BaseStream.Seek(posBeforeWrite, SeekOrigin.Begin);
      writer.Write(IPAddress.HostToNetworkOrder((int)writtenBytes));
      writer.BaseStream.Seek(posAfterWrite, SeekOrigin.Begin);

      return (UInt32)writtenBytes;
    }

    public static UInt16 WrittenBytesShort(long posBeforeWrite,
      BinaryWriter writer) {
      long posAfterWrite = writer.BaseStream.Position;
      UInt16 writtenBytes =  (UInt16)(posAfterWrite - posBeforeWrite);
      writer.BaseStream.Seek(posBeforeWrite, SeekOrigin.Begin);
      writer.Write(IPAddress.HostToNetworkOrder((short)writtenBytes));
      writer.BaseStream.Seek(posAfterWrite, SeekOrigin.Begin);

      return writtenBytes;
    }

    public static UInt16 WrittenBytesShortExcludeLength(long posBeforeWrite,
      BinaryWriter writer) {
      long posAfterWrite = writer.BaseStream.Position;
      UInt16 writtenBytes = (UInt16)(posAfterWrite - posBeforeWrite - 2);
      writer.BaseStream.Seek(posBeforeWrite, SeekOrigin.Begin);
      writer.Write(IPAddress.HostToNetworkOrder((short)writtenBytes));
      writer.BaseStream.Seek(posAfterWrite, SeekOrigin.Begin);

      return writtenBytes;
    }

    public static UInt32 WrittenBytesExcludeLength(long posBeforeWrite,
      BinaryWriter writer) {
      long posAfterWrite = writer.BaseStream.Position;
      long writtenBytes = posAfterWrite - posBeforeWrite - 4;
      writer.BaseStream.Seek(posBeforeWrite, SeekOrigin.Begin);
      writer.Write(IPAddress.HostToNetworkOrder((int)writtenBytes));
      writer.BaseStream.Seek(posAfterWrite, SeekOrigin.Begin);

      return (UInt32)writtenBytes;
    }

    /// <summary>
    /// Return the amount in bytes of data read already by the reader starting
    /// by posBeforeRead.
    /// </summary>
    /// <param name="posBeforeRead">Position befored read</param>
    /// <param name="reader">The binary reader</param>
    /// <returns></returns>
    public static UInt32 ReadBytes(long posBeforeRead, BinaryReader reader) {
      long posAfterRead = reader.BaseStream.Position;
      long readBytes = posAfterRead - posBeforeRead;
      return (UInt32)readBytes;
    }
  }

  public static class X509Utils
  {

      public static bool VerifyCertificate(X509Certificate2 local, X509Certificate2 root)
      {
          var chain = new X509Chain();
          chain.ChainPolicy.ExtraStore.Add(root);

          // ignore certificate revokation
          chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

          // preliminary validation
          if (!chain.Build(local))        
              return false;

          // make sure all the thumbprints of the CAs match up
          for (var i = 1; i < chain.ChainElements.Count; i++)
          {
              if (chain.ChainElements[i].Certificate.Thumbprint != chain.ChainPolicy.ExtraStore[i - 1].Thumbprint)
                  return false;
          }

          return true;
      }
  }
}