using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public class IdDecryptor(IEncryptionService encryptionService) : IValueConverter<string, int>
    {
        public int Convert(string sourceMember, ResolutionContext context)
        {
            var decryptedString = encryptionService.Decrypt(sourceMember);
            if (!int.TryParse(decryptedString, out _))
            {
                throw new AutoMapperMappingException($"Decrypted ID '{decryptedString}' is not a valid integer.");
            }
            
            return int.Parse(decryptedString);
        }
    }
}