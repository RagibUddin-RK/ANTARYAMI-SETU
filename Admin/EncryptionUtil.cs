using System;
using System.IO;
using System.Security.Cryptography;

namespace AntaryamiSetuAdmin
{
    public static class EncryptionUtil
    {
        // 32-byte key for AES-256
        private static readonly byte[] Key = new byte[] 
        { 
            0x41, 0x6E, 0x74, 0x61, 0x72, 0x79, 0x61, 0x6D, 
            0x69, 0x53, 0x65, 0x74, 0x75, 0x5F, 0x53, 0x65, 
            0x63, 0x72, 0x65, 0x74, 0x4B, 0x65, 0x79, 0x32, 
            0x35, 0x36, 0x42, 0x69, 0x74, 0x73, 0x21, 0x21 
        }; // "AntaryamiSetu_SecretKey256Bits!!"

        // 16-byte IV for AES
        private static readonly byte[] IV = new byte[] 
        { 
            0x41, 0x6E, 0x74, 0x61, 0x72, 0x79, 0x61, 0x6D, 
            0x69, 0x5F, 0x49, 0x56, 0x5F, 0x31, 0x36, 0x42 
        }; // "Antaryami_IV_16B"

        public static byte[] Encrypt(byte[] plainBytes)
        {
            if (plainBytes == null || plainBytes.Length == 0) return Array.Empty<byte>();

            using (Aes aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var msEncrypt = new MemoryStream())
                {
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(plainBytes, 0, plainBytes.Length);
                        csEncrypt.FlushFinalBlock();
                    }
                    return msEncrypt.ToArray();
                }
            }
        }

        public static byte[] Decrypt(byte[] cipherBytes)
        {
            if (cipherBytes == null || cipherBytes.Length == 0) return Array.Empty<byte>();

            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = Key;
                    aes.IV = IV;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var msDecrypt = new MemoryStream(cipherBytes))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var msPlain = new MemoryStream())
                    {
                        csDecrypt.CopyTo(msPlain);
                        return msPlain.ToArray();
                    }
                }
            }
            catch
            {
                // If decryption fails (e.g. wrong key, corrupted data), return empty array
                return Array.Empty<byte>();
            }
        }
    }
}
