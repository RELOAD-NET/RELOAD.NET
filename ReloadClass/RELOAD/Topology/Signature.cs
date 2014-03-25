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
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Net;
using System.Reflection;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Storage;

using System.Security.Cryptography.X509Certificates;
using TSystems.RELOAD.Transport;

namespace TSystems.RELOAD.Topology {

  /// <summary>
  /// See,
  /// http://www.iana.org/assignments/tls-parameters/tls-parameters.xml
  /// </summary>
  public enum HashAlgorithm {
    node = (byte)0,
    md5 = (byte)1,
    sha1 = (byte)2,
    sha224 = (byte)3,
    sha256 = (byte)4,
    sha384 = (byte)5,
    sha512 = (byte)6
  }

  /// <summary>
  /// TLS SignatureAlgorithm Registry
  ///
  ///  Reference
  ///       [RFC5246]
  /// Range  Registration Procedures
  /// 0-63   Standards Action
  /// 64-223  Specification Required
  /// 224-255 Reserved for Private Use
  /// 
  /// Encode this as byte
  /// </summary>
  public enum SignatureAlgorithm {
    anonymous = (byte)0,
    rsa       = (byte)1,
    dsa       = (byte)2,
    ecdas     = (byte)3
  }

  /// <summary>
  /// Each SignatureAndHashAlgorithm value lists a single hash/signature
  /// pair that the client is willing to verify.  The values are indicated
  /// in descending order of preference.
  /// 
  /// see RFC-5246 p.46
  /// </summary>
  public struct SignatureAndHashAlgorithm {
    public HashAlgorithm hash;
    public SignatureAlgorithm signature;

    public SignatureAndHashAlgorithm(HashAlgorithm hash,
      SignatureAlgorithm signature) {
      this.hash = hash;
      this.signature = signature;
    }

  }

  /// <summary>
  /// See,
  /// http://www.iana.org/assignments/tls-parameters/tls-parameters.xml
  /// </summary>
  public enum SignerIdentityType {
    reservedSignerIdentity  = 0,
    cert_hash               = 1,
    cert_hash_node_id       = 2,
    none                    = 3
  }

  /// <summary>
  /// The type of the certificate, as defined in [RFC6091].  Only the
  /// use of X.509 certificates is defined in this draft.
  /// </summary>
  public enum CertificateType {
    X509    = 0,
    OpenPGP = 1
  }

  /// <summary>
  /// This class represents the Signature struct.
  /// See RELOAD base -13 p. 52
  /// </summary>
  public class Signature {

    #region Properties

    ReloadConfig m_ReloadConfig;

    private SignatureAndHashAlgorithm algorithm;
    /// <summary>
    /// The signature algorithm in use.  The algorithm definitions are
    /// found in the IANA TLS SignatureAlgorithm Registry and
    /// HashAlgorithm registries.  All implementations MUST support
    /// RSASSA-PKCS1-v1_5 [RFC3447] signatures with SHA-256 hashes.
    /// </summary>
    public SignatureAndHashAlgorithm Algorithm {
      get { return algorithm; }
    }

    private SignerIdentity identity;
    /// <summary>
    /// The identity used to form the signature.
    /// </summary>
    public SignerIdentity Identity {
      get { return identity; }
    }

    private byte[] signatureValue;
    /// <summary>
    /// The value of the signature.
    /// </summary>
    public byte[] SignaureValue {
      get { return signatureValue; }
    }

    #endregion   

    #region Constructors

    /// <summary>
    /// Use this contructor to obtain signature from bytes.
    /// </summary>
    /// <param name="config">The stack configuration</param>
    public Signature(ReloadConfig config) {
      m_ReloadConfig = config;
    }

    /// <summary>
    /// For signatures over messages the input to the signature is computed
    /// over the overlay and transaction_id come from the forwarding header
    /// see RELOAD base -13 p.53
    /// </summary>
    /// <param name="overlay">overlay</param>
    /// <param name="transaction_id">transaction_id</param>
    /// <param name="messageContents">Message Contents</param>
    /// <param name="signerIdentity">SignerIdentity</param>
    public Signature(UInt32 overlay, string transactionId, string messageContents, SignerIdentity signerIdentity, ReloadConfig config) {

      m_ReloadConfig = config;

      algorithm = new SignatureAndHashAlgorithm(HashAlgorithm.sha256,
        ReloadGlobals.SignatureAlg);
      identity = signerIdentity;
      /* Compute signature */      
      String signaturInput = String.Format("{0}{1}{2}{3}", overlay, transactionId, messageContents, identity.ToString());

      signatureValue = Sign(signaturInput);      
    }

    public Signature(UInt32 overlay, string transactionId, byte[] messageContents, SignerIdentity signerIdentity, ReloadConfig config)
    {
        m_ReloadConfig = config;

        algorithm = new SignatureAndHashAlgorithm(HashAlgorithm.sha256,
          ReloadGlobals.SignatureAlg);
        identity = signerIdentity;
        /* Compute signature */

        byte[] bOverlay = BitConverter.GetBytes(overlay);
        byte[] bTransId = Encoding.Unicode.GetBytes(transactionId);
        byte[] bId = Encoding.Unicode.GetBytes(identity.ToString());

        byte[] sig = new byte[bOverlay.Length + bTransId.Length + messageContents.Length + bId.Length];
        System.Buffer.BlockCopy(bOverlay, 0, sig, 0, bOverlay.Length);
        System.Buffer.BlockCopy(bTransId, 0, sig, bOverlay.Length, bTransId.Length);
        System.Buffer.BlockCopy(messageContents, 0, sig, bOverlay.Length + bTransId.Length, messageContents.Length);
        System.Buffer.BlockCopy(bId, 0, sig, bOverlay.Length + bTransId.Length + messageContents.Length, bId.Length);

        signatureValue = Sign(sig);
    }

    /// <summary>
    /// Each StoredData element is individually signed.  However, the
    /// signature also must be self-contained and cover the Kind-ID and
    /// Resource-ID even though they are not present in the StoredData
    /// structure.  The input to the signature algorithm is:
    /// resource_id || kind || storage_time || StoredDataValue ||
    /// SignerIdentity
    /// </summary>
    /// <param name="resId"></param>
    /// <param name="kind"></param>
    /// <param name="storageTime"></param>
    /// <param name="storedDataValue"></param>
    /// <param name="identity"></param>
    public Signature(ResourceId resId, UInt32 kind, UInt64 storageTime,
      StoredDataValue value, SignerIdentity signerIdentity,
      ReloadConfig config) {

      m_ReloadConfig = config;
      var ascii = new ASCIIEncoding();
      /* Set alogorithm and identity */
      algorithm =  new SignatureAndHashAlgorithm(HashAlgorithm.sha256,
        ReloadGlobals.SignatureAlg);
      identity = signerIdentity;
      /* Get string of stored data value */
      var ms = new MemoryStream();
      var bw = new BinaryWriter(ms);
      value.Dump(bw);
      value.GetUsageValue.dump(bw);
      ms.Position = 0;
      var sr = new StreamReader(ms);
      string strValue = sr.ReadToEnd();
      sr.Close();
      bw.Close();
      /* Concatenate signature input */
      String signaturInput = String.Format("{0}{1}{2}{3}{4}",
        ascii.GetString(resId.Data, 0, resId.Data.Length), kind, storageTime,
        strValue, identity.ToString());
      signatureValue = Sign(signaturInput);
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Obtains the private key file.
    /// </summary>
    /// <returns>private key as byte array</returns>
    private byte[] GetKeyFile() {

      string sLocalCertFilename;

      sLocalCertFilename = m_ReloadConfig.IMSI == "" ?
        "VNODE_" + m_ReloadConfig.ListenPort.ToString() : m_ReloadConfig.IMSI;

      string privateKeyFile = null;

#if COMPACT_FRAMEWORK
             // Check for OsVersion to remove URI prefix if it is not WindowsCE.
            string basex = Assembly.GetExecutingAssembly().GetName().CodeBase.ToString();
            string applicationDirectory = Path.GetDirectoryName(basex);

            string pem_file = applicationDirectory + "\\" + sLocalCertFilename + ".csr";
            string privateKey_file = applicationDirectory +  "\\" + sLocalCertFilename + ".key";
#else
      privateKeyFile = sLocalCertFilename + ".pem";
#endif

      return File.ReadAllBytes(privateKeyFile);
    }

    /// <summary>
    /// Creates the signature using RSA over a SHA256. Uses the private 
    /// key from file.
    /// </summary>
    /// <param name="signaturInput">Data to be signed.</param>
    /// <returns>the signature value</returns>
    private byte[] Sign(byte[] bSignInput)
    {
        X509Certificate2 MyCert = m_ReloadConfig.MyCertificate;

        if (!MyCert.HasPrivateKey)
            return null;

        RSACryptoServiceProvider key = (RSACryptoServiceProvider)MyCert.PrivateKey;

        byte[] signature = key.SignData(bSignInput, CryptoConfig.MapNameToOID("SHA256"));
        return signature;
    }


    /// <summary>
    /// Creates the signature using RSA over a SHA256. Uses the private 
    /// key from file.
    /// </summary>
    /// <param name="signaturInput">Data to be signed.</param>
    /// <returns>the signature value</returns>
    private byte[] Sign(string signaturInput) {

        var ascii = new ASCIIEncoding();
        byte[] bSignInput = ascii.GetBytes(signaturInput);

        X509Certificate2 MyCert = m_ReloadConfig.MyCertificate;

        if (!MyCert.HasPrivateKey)
            return null;

        RSACryptoServiceProvider key = (RSACryptoServiceProvider)MyCert.PrivateKey;

        byte[] signature = key.SignData(bSignInput, CryptoConfig.MapNameToOID("SHA256"));
        return signature;
}

    #endregion

    #region Public methods

    public UInt32 Dump(BinaryWriter writer) {
        var ascii = Encoding.ASCII;
        long posBeforeSign = writer.BaseStream.Position;
        writer.Write((byte)algorithm.hash);
        writer.Write((byte)algorithm.signature);
        /* Write identity */
        writer.Write((byte)identity.IdentityType);
        long posBeforeIdentity = writer.BaseStream.Position;
        /* Placeholder for length of identity */
        writer.Write(IPAddress.HostToNetworkOrder((short)0));
        /* Write identity value */
        writer.Write((byte)identity.Identity.HashAlg);
        writer.Write((byte)identity.Identity.CertificateHash.Length);
        writer.Write(identity.Identity.CertificateHash);
        StreamUtil.WrittenBytesShortExcludeLength(posBeforeIdentity, writer);
        /* Write signature value */
        ReloadGlobals.WriteOpaqueValue(writer, signatureValue, 0xFFFF);
        return (UInt32)(writer.BaseStream.Position - posBeforeSign);
    }

    public Signature FromReader(BinaryReader reader, long reload_msg_size) {
        var ascii = new ASCIIEncoding();
        var hashAlg = (HashAlgorithm)reader.ReadByte();
        var signatureAlg = (SignatureAlgorithm)reader.ReadByte();
        algorithm = new SignatureAndHashAlgorithm(hashAlg, signatureAlg);
        /* Read SignerIdentity */
        var type = (SignerIdentityType)reader.ReadByte();
        UInt16 length = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        /* Read SignerIdentityValue */
        hashAlg = (HashAlgorithm)reader.ReadByte();
        length -= 1;
        ushort hashLen = (ushort)reader.ReadByte();
        byte[] bHash = reader.ReadBytes(hashLen);
        /* Create SignerIdentityValue */
        var signerIdVal = new SignerIdentityValue(type, hashAlg, bHash);
        /* Create SignerIdentity */
        identity = new SignerIdentity(type, signerIdVal);
        /* Read SignatureValue */
        UInt16 sigLen = (UInt16)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        signatureValue = reader.ReadBytes(sigLen);

        return this;
    }

    #endregion

  }

  /// <summary>
  /// The identity used to form the signature.
  /// 
  /// see base -19 p. 55
  /// </summary>
  public class SignerIdentity {

    private SignerIdentityType identityType;
    public SignerIdentityType IdentityType {
      get { return identityType; }
    }

    private UInt16 length;
    public UInt16 Length {
      get { return length; }
    }

    private SignerIdentityValue identity;
    public SignerIdentityValue Identity {
      get { return identity; }
    }

    /// <summary>
    /// Creates a signer identity.
    /// </summary>
    public SignerIdentity(SignerIdentityType type, SignerIdentityValue value) {
      identityType = type;
      identity = value;

      length = (UInt16)(1 + value.CertificateHash.Length); 
    }

    /// <summary>
    /// Returns a string representation of the signer identity.
    /// </summary>
    /// <returns>A string</returns>
    public override string ToString() {
      return String.Format("{0}{1}{2}", identityType, length,
        identity.ToString());
    }

  }

  /// <summary>
  /// The identity used to form the signature.
  /// 
  /// see base -19 p. 54
  /// </summary>
  public class SignerIdentityValue {

    private HashAlgorithm hashAlg;
    /// <summary>
    /// The hash algorithm in use.
    /// </summary>
    public HashAlgorithm HashAlg {
      get { return hashAlg; }
    }

    private byte[] certificateHash;
    /// <summary>
    /// The hash over the certificate.
    /// </summary>
    public byte[] CertificateHash {
      get { return certificateHash; }
    }

    /// <summary>
    /// Create new instances of SignerIdentityValue.
    /// See RELOAD base -13 p. 52
    /// </summary>
    /// <param name="type">The Singner type: cert_hash</param>
    /// <param name="args">The arguments for this type, e.g., cert hash: args[0] = hash_alg, args[1]= certificate_hash</param>
    public SignerIdentityValue(SignerIdentityType type, HashAlgorithm alg, byte[] hash) {
      switch (type) {
        case SignerIdentityType.cert_hash:          
            hashAlg = alg;
            certificateHash = (byte[])hash.Clone();          
          break;
        case SignerIdentityType.cert_hash_node_id:
          throw new NotSupportedException(
            "ReloadMsg: SignIDType cert_hash_node_id not implemented!");
        /* This structure may be extended with new types if necessary*/
        default:
          throw new NotImplementedException(
            String.Format("SigIdValue: Uknown SignerIdentityType: {0}", type));
      }
    }

    /// <summary>
    /// Returns a string representation of signer Id value.
    /// </summary>
    /// <returns>A string</returns>
    public override string ToString() {
      return String.Format("{0}{1}",hashAlg, certificateHash);
    }
  }

  /// <summary>
  /// A bucket of certificates.
  /// 
  /// see base -19 p.52
  /// </summary>
  public class GenericCertificate {

    ~GenericCertificate()
    {
        certificate = null;
    }

    private CertificateType type;
    /// <summary>
    /// Returns the type of PKC, hence X.509
    /// </summary>
    public CertificateType Type {
      get { return type; }
    }

    private byte[] certificate = null;
    /// <summary>
    /// Returns the PKC in an opaque string representation.
    /// </summary>
    public byte[] Certificate
    {
      get { return certificate; }      
    }

    public GenericCertificate(byte[] pkc)
    {
      if (pkc == null)
        throw new ArgumentNullException("GenericCert: pkc null!");
      type = CertificateType.X509; // the default in RELOAD
      certificate = (byte[])pkc.Clone();
    }     
  }

}
