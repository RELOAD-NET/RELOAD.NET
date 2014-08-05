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
using CERTENROLLLib;
using System.Security.Cryptography;

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
          if(local.Issuer != local.Subject) // not self signed?
            chain.ChainPolicy.ExtraStore.Add(root);
          
          if (local.Issuer == local.Subject) // self signed? 
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

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



      /*
       * http://stackoverflow.com/questions/13806299/how-to-create-a-self-signed-certificate-using-c
       * This implementation uses the CX509CertificateRequestCertificate COM object (and friends - MSDN doc) from certenroll.dll to create a self signed certificate request and sign it. 
       */
      public static X509Certificate2 CreateSelfSignedCertificateCOM(string subjectName)
      {
          // create DN for subject and issuer
          var dn = new CX500DistinguishedName();
          dn.Encode("CN=" + subjectName, X500NameFlags.XCN_CERT_NAME_STR_NONE);

          // create a new private key for the certificate
          CX509PrivateKey privateKey = new CX509PrivateKey();
          privateKey.ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider";
          privateKey.MachineContext = true;
          privateKey.Length = 2048;
          privateKey.KeySpec = X509KeySpec.XCN_AT_SIGNATURE; // use is not limited
          privateKey.ExportPolicy = X509PrivateKeyExportFlags.XCN_NCRYPT_ALLOW_PLAINTEXT_EXPORT_FLAG;
          privateKey.KeyUsage = X509PrivateKeyUsageFlags.XCN_NCRYPT_ALLOW_SIGNING_FLAG;
          privateKey.Create();

          var hashobj = new CObjectId();
          hashobj.InitializeFromAlgorithmName(ObjectIdGroupId.XCN_CRYPT_HASH_ALG_OID_GROUP_ID,
              ObjectIdPublicKeyFlags.XCN_CRYPT_OID_INFO_PUBKEY_ANY,
              AlgorithmFlags.AlgorithmFlagsNone, "SHA256");

          // add extended key usage if you want - look at MSDN for a list of possible OIDs
          //var oid = new CObjectId();
          //oid.InitializeFromValue("1.3.6.1.5.5.7.3.1"); // SSL server
          //var oidlist = new CObjectIds();
          //oidlist.Add(oid);
          //var eku = new CX509ExtensionEnhancedKeyUsage();
          //eku.InitializeEncode(oidlist);

          // Create the self signing request
          var cert = new CX509CertificateRequestCertificate();
          cert.InitializeFromPrivateKey(X509CertificateEnrollmentContext.ContextMachine, privateKey, "");
          cert.Subject = dn;
          cert.Issuer = dn; // the issuer and the subject are the same
          cert.NotBefore = DateTime.Now.Subtract(new TimeSpan(1, 0, 0, 0));
          cert.NotAfter = DateTime.Now.Add(new TimeSpan(30,0,0,0));
          //cert.X509Extensions.Add((CX509Extension)eku); // add the EKU
          cert.HashAlgorithm = hashobj; // Specify the hashing algorithm
          cert.Encode(); // encode the certificate

          // Do the final enrollment process
          var enroll = new CX509Enrollment();
          enroll.InitializeFromRequest(cert); // load the certificate
          enroll.CertificateFriendlyName = subjectName; // Optional: add a friendly name
          string csr = enroll.CreateRequest(); // Output the request in base64
          // and install it back as the response
          enroll.InstallResponse(InstallResponseRestrictionFlags.AllowUntrustedCertificate,
              csr, EncodingType.XCN_CRYPT_STRING_BASE64, ""); // no password
          // output a base64 encoded PKCS#12 so we can import it back to the .Net security classes
          var base64encoded = enroll.CreatePFX("", // no password, this is for internal consumption
              PFXExportOptions.PFXExportChainWithRoot);

          // instantiate the target class with the PKCS#12 data (and the empty password)
          return new System.Security.Cryptography.X509Certificates.X509Certificate2(
              System.Convert.FromBase64String(base64encoded), "",
              // mark the private key as exportable (this is usually what you want to do)
              System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable
          );
      }
  }
}