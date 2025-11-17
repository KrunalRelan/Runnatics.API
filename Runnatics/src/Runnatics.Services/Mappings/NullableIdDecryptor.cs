using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public class NullableIdDecryptor(IEncryptionService encryptionService) : IValueConverter<string, int?>
    {
        public int? Convert(string sourceMember, ResolutionContext context) => string.IsNullOrWhiteSpace(sourceMember) ? null : int.Parse(encryptionService.Decrypt(sourceMember));
    }
}