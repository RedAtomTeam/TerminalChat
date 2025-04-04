using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TerminalChat
{
    internal class AesGcmHelper
    {
        public static (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(string plaintext, byte[] key)
        {
            using var aes = new AesGcm(key);

            // Генерируем одноразовый nonce (12 байт для AES-GCM)
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);

            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] ciphertext = new byte[plaintextBytes.Length];
            byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 байт

            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            return (ciphertext, nonce, tag);
        }

        public static string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] key)
        {
            using var aes = new AesGcm(key);
            byte[] plaintext = new byte[ciphertext.Length];

            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
    }
}
