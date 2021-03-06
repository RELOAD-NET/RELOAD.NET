﻿/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
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
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Utils;
using TSystems.RELOAD.Storage;

namespace TSystems.RELOAD.Topology {
  /// <summary>
  /// Represents a RELOAD access control policy
  /// 
  /// see base -19 p.
  /// </summary>
  public interface IAccessControlPolicy {

    /// <summary>
    /// The well-known name of the ACP, e.g., USER-MATCH
    /// </summary>
    String NAME { get; }

    /// <summary>
    /// This method validates if an ACP applies.
    /// </summary>
    /// <param name="resId">The requested resId</param>
    /// <param name="resId">The KindId</param>
    /// <param name="data">The StoredData</param>
    /// <returns>True, if ACP applies</returns>
    Boolean ValuePermitted(ResourceId resId, UInt32 kindId, StoredData data);
  }

  public interface IAccessController {

    /// <summary>
    /// An access controller should be aware of its own identity ;-)
    /// </summary>
    SignerIdentity MyIdentity { get; }

    /// <summary>
    /// Returns the PKC to the corresponding signer identity
    /// </summary>
    /// <param name="identity">The signer identity</param>
    /// <returns>The corresponding X.509 PKC as opaque string</returns>
    GenericCertificate GetPKC(SignerIdentity identity);

    /// <summary>
    /// Returns a list of PKCs corresponding to signer identities
    /// </summary>
    /// <param name="identities">A list of signer identities</param>
    /// <returns>The corresponding list of X.509 PKCs as opaque strings
    /// </returns>
    List<GenericCertificate> GetPKCs(List<SignerIdentity> identities);

    /// <summary>
    /// Stores a list of X.509 certificates.
    /// </summary>
    /// <param name="pkcs">Lisf of X.509 PKCs as opaque strings</param>
    void SetPKCs(List<GenericCertificate> pkcs);

    /// <summary>
    /// Stores a list of X.509 certificates.
    /// </summary>
    /// <param name="pkcs">Lisf of X.509 PKCs as opaque strings</param>
    void SetPKCs(Dictionary<String, GenericCertificate> pkcs);

    /// <summary>
    /// Parses the config document for access control codes and
    /// adds these policies the ACP pool.
    /// </summary>
    /// <param name="config">The configuration document</param>    
    void FromConfig(configuration config);

    /// <summary>
    /// Register a new access control policy to the stack
    /// </summary>
    /// <param name="acp">An instance of IAccessControlPolicy</param>
    void RegisterPolicy(IAccessControlPolicy acp);

    /// <summary>
    /// Returns the corresponding access control policy to kind-id
    /// </summary>
    /// <param name="kindId">The kind-id</param>
    /// <returns>An access control policy</returns>
    IAccessControlPolicy GetACP(UInt32 kindId);

    /// <summary>
    /// Validates the request originator by its PKC
    /// </summary>
    /// <param name="msg">The request to be checked</param>
    /// <returns>True, if the originator is validated</returns>
    Boolean RequestPermitted(ReloadMessage msg);

    /// <summary>
    /// Validates a StoredData using the PKCs. RequestPermitted
    /// should be called first to set the PKCs
    /// </summary>
    /// <param name="resId">The resId of the Request</param>
    /// <param name="kind">The kind of the Response</param>
    /// <param name="sd">StoredData to verify</param>
    /// <returns>True, if the signature is validated</returns>
    Boolean validateDataSignature(ResourceId resId, uint kind, StoredData sd);

  }

  public class AccessController : IAccessController {

    #region Private members

    /* key = hash over PKC, value = PKC */
    private Dictionary<String, GenericCertificate> storedPKCs;
    /* The configuration of this stack */
    ReloadConfig m_ReloadConfig;
    /* The registered access control policies 
     * key = ACP name, value = ACP
     */
    Dictionary<String, IAccessControlPolicy> ACPs;
    /* Maps the relation from kind-id to ACP
     * key = kind-id, value = name of ACP
     */
    Dictionary<UInt32, String> ACPmap;

    #endregion

    private SignerIdentity myIdentity;
    /// <summary>
    ///  The signer identity of this peer/client
    /// </summary>
    public SignerIdentity MyIdentity {
      get { return myIdentity; }
    }

    public AccessController(ReloadConfig rc) {
      var ascii = new ASCIIEncoding();
      m_ReloadConfig = rc;
      storedPKCs = new Dictionary<string, GenericCertificate>();
      ACPs = new Dictionary<String, IAccessControlPolicy>();
      ACPmap = new Dictionary<UInt32, String>();
      /* Convert My TEIX509Certificate to opaque string*/
      /* SignerIdValue*/
      var sha256 = new SHA256Managed();
      byte[] myCert;
      //m_ReloadConfig.MyCertificate.SaveToBuffer(out myCert);
      //myCert = m_ReloadConfig.MySSLCertificate.DER;
      myCert = m_ReloadConfig.MyCertificate.GetRawCertData();
      byte[] bHash = sha256.ComputeHash(myCert);
      var signIdVal = new SignerIdentityValue(SignerIdentityType.cert_hash,
        ReloadGlobals.HashAlg, bHash);

      /* Publish my Id and my PKC */
      var myGenCert = new GenericCertificate(myCert);
      myIdentity = new SignerIdentity(SignerIdentityType.cert_hash, signIdVal);
      string strHash = String.Join(String.Empty, bHash.Select(b => b.ToString("x2")));
      storedPKCs.Add(strHash, myGenCert);
    }

    private bool validateCertHash(ReloadMessage msg, X509Certificate2 signerCert)
    {
        //var ascii = Encoding.ASCII;
        UInt32 overlay = msg.forwarding_header.overlay;
        UInt64 transId = msg.forwarding_header.transaction_id;

        /* Convert msg body to string */
        var ms = new MemoryStream();
        var br = new BinaryWriter(ms);
        msg.reload_message_body.Dump(br);
        ms.Position = 0;

        //var sr = new StreamReader(ms);
        //string msgBoby = sr.ReadToEnd();

        byte[] msgBody = ms.ToArray();

        br.Close();

        /* Covert Idenity to string */
        String identity = msg.security_block.Signature.Identity.ToString();

        /* Concatenate signature params*/
        //String strSignaturInput = String.Format("{0}{1}{2}{3}", overlay, transId, msgBody, identity);

        //byte[] signatureInput = ascii.GetBytes(strSignaturInput);

        byte[] bOverlay = BitConverter.GetBytes(overlay);
        byte[] bTransId = Encoding.Unicode.GetBytes(transId.ToString());
        byte[] bId = Encoding.Unicode.GetBytes(identity);

        byte[] signatureInput = new byte[bOverlay.Length + bTransId.Length + msgBody.Length + bId.Length];
        System.Buffer.BlockCopy(bOverlay, 0, signatureInput, 0, bOverlay.Length);
        System.Buffer.BlockCopy(bTransId, 0, signatureInput, bOverlay.Length, bTransId.Length);
        System.Buffer.BlockCopy(msgBody, 0, signatureInput, bOverlay.Length + bTransId.Length, msgBody.Length);
        System.Buffer.BlockCopy(bId, 0, signatureInput, bOverlay.Length + bTransId.Length + msgBody.Length, bId.Length);

        byte[] sigVal = msg.security_block.Signature.SignaureValue;

        if (msg.security_block.Signature.Algorithm.signature != SignatureAlgorithm.rsa)
            throw new NotImplementedException("AccessController:" +
                  "encryption not implemented!");

        if (msg.security_block.Signature.Algorithm.hash != HashAlgorithm.sha256)
            throw new NotImplementedException("AccessController:" +
                  "hash algoritm not implemented!");

        try
        {
            RSACryptoServiceProvider rsaCSP = (RSACryptoServiceProvider)signerCert.PublicKey.Key;

            if (rsaCSP.VerifyData(signatureInput, CryptoConfig.MapNameToOID("SHA256"), msg.security_block.Signature.SignaureValue))
                return true;
            else
            {
                if (msg.reload_message_body.RELOAD_MsgCode == RELOAD_MessageCode.Attach_Answer)
                {
                    // Use this log message to compare with the log message in Topology.Signature.Signature()
                    string msgBodyLog = "";
                    foreach (byte b in msgBody)
                        msgBodyLog += Convert.ToString(b, 16);
                    m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_INFO, String.Format("validateCertHash(): TransId: {0:x} messageContent: {1}", transId, msgBodyLog));
                }
                return false;
            }
        }
        catch (Exception e)
        {
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR, String.Format("VerifyHash: {0}", e.Message));
            return false;
        }
    }



    #region Public methods

    public GenericCertificate GetPKC(SignerIdentity identity) {
      if (identity == null)
        throw new ArgumentNullException(
          "AccessControl.GetPKC: Identity null");
      string strHash = String.Join(String.Empty, identity.Identity.CertificateHash.Select(b => b.ToString("x2")));
      return storedPKCs[strHash];
    }

    public List<GenericCertificate> GetPKCs(List<SignerIdentity> identities) {
      var result = new List<GenericCertificate>();
      foreach (SignerIdentity id in identities) {
        result.Add(GetPKC(id));
      }
      return result;
    }

    public void SetPKCs(List<GenericCertificate> pkcs) {
        var certs = new Dictionary<string, GenericCertificate>();
        var sha256 = new SHA256Managed();
        
        foreach (GenericCertificate cert in pkcs)
        {
            byte[] bHash = sha256.ComputeHash(cert.Certificate);
            string strHash = String.Join(String.Empty, bHash.Select(b => b.ToString("x2")));
            if(!storedPKCs.ContainsKey(strHash))
                certs.Add(strHash, cert);
        }

        SetPKCs(certs);
    }

    public void SetPKCs(Dictionary<String, GenericCertificate> pkcs) {
      if (pkcs == null)
        throw new ArgumentNullException(
          "AccessController.SetPKCs: PKCs are null!");
      foreach (String hash in pkcs.Keys) {
        if (!storedPKCs.ContainsKey(hash)) {
          storedPKCs.Add(hash, pkcs[hash]);
        }
      }
    }

    public void FromConfig(configuration config) {
      kindblock[] reqKinds = config.requiredkinds;
      foreach (kindblock reqKind in reqKinds) {
        string acp = reqKind.kind.accesscontrol;
        if (reqKind.kind.name != null) {
          /* TODO should be handled by an XML or something else */
          switch (reqKind.kind.name) {
            case "SIP-REGISTRATION":
              ACPmap.Add(12, reqKind.kind.accesscontrol.ToUpper());
              break;
            case "DISCO-REGISTRATION":
              ACPmap.Add(16, reqKind.kind.accesscontrol.ToUpper());
              break;
            case "ACCESS-CONTROL-LIST":
              ACPmap.Add(17, reqKind.kind.accesscontrol.ToUpper());
              break;
            default:
              // Nothing, ignore...
              break;
          }
        }
        else {
          UInt32 kindId = reqKind.kind.id;
          ACPmap.Add(kindId, reqKind.kind.accesscontrol.ToUpper());
        }
      }
    }

    public void RegisterPolicy(IAccessControlPolicy acp) {
      if (acp == null)
        throw new ArgumentNullException(
          "AccessController.RegisterPolicy: ACP is null!");
      ACPs.Add(acp.NAME.ToUpper(), acp);
    }

    public IAccessControlPolicy GetACP(uint kindId) {

      return ACPs[ACPmap[kindId]];
    }

    /// <summary>
    /// Moked! Must be implemented
    /// </summary>
    /// <param name="msg"></param>
    /// <returns></returns>
    public bool RequestPermitted(ReloadMessage msg) {
      var ascii = new ASCIIEncoding();
      /* Obtain security parameter */
      SecurityBlock security_block = msg.security_block;
      var signId = security_block.Signature.Identity;
      /* Dictionary over all certs carried in msg */
      var certs = new Dictionary<string, GenericCertificate>();
      var sha256 = new SHA256Managed();

      /* Store all certificates */
      foreach (GenericCertificate pkc in security_block.Certificates) {
        byte[] bHash = sha256.ComputeHash(pkc.Certificate);
        string strHash = String.Join(String.Empty, bHash.Select(b => b.ToString("x2")));
        if(!certs.ContainsKey(strHash))
            certs.Add(strHash, pkc);

      }
      SetPKCs(certs);
      string strHash2 = String.Join(String.Empty, signId.Identity.CertificateHash.Select(b => b.ToString("x2")));

      byte[] bcert = certs[strHash2].Certificate;
      //cert.LoadFromBuffer(bcert);

      // Validate Certificate with CA
      /* Certificate from originator */
      X509Certificate2 cert = new X509Certificate2(bcert);
      if (!Utils.X509Utils.VerifyCertificate(cert,m_ReloadConfig.RootCertificate))
      {
        //m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
        //  String.Format("FromBytes: NodeID {0}, Certificate" +
        //  "validation failed (CA Issuer {1})",
        //  security_block.OriginatorNodeID, cert.IssuerName.CommonName));
            m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
              String.Format("FromBytes: NodeID {0}, Certificate" +
              "validation failed (CA Issuer {1})",
              security_block.OriginatorNodeID, cert.Issuer));
        return false;
      }

      /* Validate Signature */
      switch (signId.IdentityType) {
        case SignerIdentityType.cert_hash:
            return validateCertHash(msg, cert);
        case SignerIdentityType.cert_hash_node_id:
          // TODO
          throw new NotImplementedException(
            "ReloadMsg: SignIDType cert_hash_node_id not implemented!");

        case SignerIdentityType.none:
          break;
        default:
          throw new NotSupportedException(String.Format(
            "ReloadMsg: The signer identity type {0} is not supported",
            signId.IdentityType));
      }

      return false;
    }

    public bool validateDataSignature(ResourceId resId, uint kind, StoredData sd) {
      //FetchAns fetch_answer = (FetchAns)(reloadMsg.reload_message_body);

      var ascii = new ASCIIEncoding();
      /* Set alogorithm and identity */
      SignatureAndHashAlgorithm algorithm = new SignatureAndHashAlgorithm(HashAlgorithm.sha256, ReloadGlobals.SignatureAlg);
      /* Covert Idenity to string */
      String identity = sd.Signature.Identity.ToString();
      /* Get string of stored data value */
      var ms = new MemoryStream();
      var bw = new BinaryWriter(ms);
      sd.Value.Dump(bw);
      sd.Value.GetUsageValue.dump(bw);
      ms.Position = 0;
      var sr = new StreamReader(ms);
      string strValue = sr.ReadToEnd();
      sr.Close();
      bw.Close();
      /* Concatenate signature input */
      String strSignaturInput = String.Format("{0}{1}{2}{3}{4}",
        ascii.GetString(resId.Data, 0, resId.Data.Length), kind, sd.StoreageTime,
        strValue, identity);

      byte[] signatureInput = ascii.GetBytes(strSignaturInput);
      byte[] sigVal = sd.Signature.SignaureValue;

      GenericCertificate gencert = GetPKC(sd.Signature.Identity);
      byte[] bcert = gencert.Certificate; //TODO: TEST
      X509Certificate2 signerCert = new X509Certificate2(bcert);

      if (!Utils.X509Utils.VerifyCertificate(signerCert, m_ReloadConfig.RootCertificate))
      {
        m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
          String.Format("validateDataSignatures: NodeID {0}, Certificate" +
          "validation failed (CA Issuer {1})",
          null, signerCert.Issuer));
        //return false;
      }
      var cert = new X509Certificate2(bcert);

      switch (sd.Signature.Algorithm.signature) {
        case SignatureAlgorithm.rsa:
          var cryptoIPT = (RSACryptoServiceProvider)cert.PublicKey.Key;

          switch (sd.Signature.Algorithm.hash) {
            case HashAlgorithm.sha256:
              var sha256 = new SHA256CryptoServiceProvider();

              if (!cryptoIPT.VerifyData(signatureInput, sha256, sigVal)) {
                throw new InvalidOperationException("Invalid signature");
                return false;
              }
              else {
                m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_FORWARDING, "DATA SIGNATURE VALID!!");
                return true;
              }
              break;
            default:
              throw new NotImplementedException("AccessController:" +
                "hash algoritm not implemented!");
          }

          break;
        case SignatureAlgorithm.dsa:
          throw new NotImplementedException("AccessController:" +
            "DSA encryption not implemented!");
        default:
          throw new NotImplementedException("AccessController:" +
            "encryption not implemented!");
      }
    }
    #endregion
  }


  public class UserNodeMatchAccessControlPolicy : IAccessControlPolicy {

    /// <summary>
    /// The well-known name of the ACP, e.g., USER-MATCH
    /// </summary>
    public String NAME {
      get { return "USER-NODE-MATCH"; }
    }


    /// <summary>
    /// This method validates if an ACP applies.
    /// </summary>
    /// <param name="resId">The requested resId</param>
    /// <param name="resId">The KindId</param>
    /// <param name="data">The StoredData</param>
    /// <returns>True, if ACP applies</returns>
    public Boolean ValuePermitted(ResourceId resId, UInt32 kindId, StoredData data) {
      //var nodeid = ReloadGlobals.retrieveNodeIDfromCertificate( signerCert, ref rfc822Name);


      return true;

    }
  }

}
