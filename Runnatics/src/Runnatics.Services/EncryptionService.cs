using Runnatics.Repositories.Interface;

namespace Runnatics.Services
{
    public class EncryptionService : SimpleServiceBase, IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;
        
        public AesEncryptionService(string encryptionKey)
        {
            if (string.IsNullOrWhiteSpace(encryptionKey))
                throw new ArgumentException("Encryption key cannot be null or empty", nameof(encryptionKey));

            using var sha256 = SHA256.Create();
            var keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey));
            _key = keyBytes;

            var ivSource = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey + "_iv"));
            _iv = ivSource[..16];
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentException("Plain text cannot be null or empty", nameof(plainText));

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(encryptedBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");
        }

        public string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                throw new ArgumentException("Encrypted text cannot be null or empty", nameof(encryptedText));

            try
            {
                var base64 = encryptedText.Replace('-', '+').Replace('_', '/');
                var padding = (4 - base64.Length % 4) % 4;
                if (padding > 0)
                    base64 += new string('=', padding);

                var encryptedBytes = Convert.FromBase64String(base64);

                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decrypt the data", ex);
            }
        }
    }
}