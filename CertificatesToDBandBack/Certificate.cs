using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace CertificatesToDBandBack
{
    public class Certificate
    {
        public Certificate() { }

        public Certificate(string cert, string key, string password)
        {
            this.PublicCertificate = cert;
            this.PrivateKey = key;
            this.Password = password;
        }

        #region Fields
        private string _publicCertificate;
        private string _privateKey;
        private string  _password;
        #endregion

        #region Properties
        public string PublicCertificate
        {
            get { return _publicCertificate; }
            set { _publicCertificate = value; }
        }

        public string PrivateKey
        {
            get { return _privateKey; }
            set { _privateKey = value; }
        }

        public string Password
        {
            get { return _password; }
            set { _password = value; }
        } 
        #endregion

        public X509Certificate2 GetCertificateFromPEMstring(bool certOnly)
        {
            if (certOnly)
                return GetCertificateFromPEMstring(this.PublicCertificate);
            else
                return GetCertificateFromPEMstring(this.PublicCertificate, this.PrivateKey, this.Password);
        }

        public static X509Certificate2 GetCertificateFromPEMstring(string publicCert)
        {
            return new X509Certificate2(Encoding.UTF8.GetBytes(publicCert));
        }

        public static X509Certificate2 GetCertificateFromPEMstring(string publicCert, string privateKey, string password)
        {
            byte[] certBuffer = Helpers.GetBytesFromPEM(publicCert, PemStringType.Certificate);
            byte[] keyBuffer  = Helpers.GetBytesFromPEM(privateKey, PemStringType.RsaPrivateKey);

            X509Certificate2 certificate = new X509Certificate2(certBuffer, password);

            RSACryptoServiceProvider prov = Crypto.DecodeRsaPrivateKey(keyBuffer);
            certificate.PrivateKey = prov;

            return certificate;
        }

    }
}
