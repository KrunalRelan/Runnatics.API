using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public class NullableIdEncryptor(IEncryptionService encryptionService) : IValueConverter<int?, string>
    {
        public string Convert(int? sourceMember, ResolutionContext context) => sourceMember.HasValue ? encryptionService.Encrypt(sourceMember.Value.ToString()) : string.Empty;
    }
}