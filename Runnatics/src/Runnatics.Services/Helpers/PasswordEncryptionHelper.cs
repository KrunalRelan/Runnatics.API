using System.Security.Cryptography;
using System.Text;

namespace Runnatics.Services.Helpers
{
    public static class PasswordEncryptionHelper
    {
        public static string Decrypt(string encryptedPassword, string base64Key)
        {
            var parts = encryptedPassword.Split(':');
            if (parts.Length != 2)
                return encryptedPassword; // not encrypted, return as-is

            var iv = Convert.FromBase64String(parts[0]);
            var ciphertext = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(base64Key);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
