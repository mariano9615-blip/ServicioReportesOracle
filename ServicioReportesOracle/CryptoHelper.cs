using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ServicioOracleReportes
{
    public static class CryptoHelper
    {
        private const string Prefix = "ENC:";
        // 32 bytes para AES-256, 16 bytes para IV
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("SrvRptOracle2024!@#$%^&*ABCDEFGH");
        private static readonly byte[] IV  = Encoding.UTF8.GetBytes("OracleReport!16b");

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV  = IV;
                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    var bytes = Encoding.UTF8.GetBytes(plainText);
                    cs.Write(bytes, 0, bytes.Length);
                    cs.FlushFinalBlock();
                    return Prefix + Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix)) return value;
            var cipherBytes = Convert.FromBase64String(value.Substring(Prefix.Length));
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV  = IV;
                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream(cipherBytes))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        public static bool IsEncrypted(string value)
            => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix);
    }
}
