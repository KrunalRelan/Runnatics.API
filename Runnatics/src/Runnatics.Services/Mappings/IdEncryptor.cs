using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public  class IdEncryptor(IEncryptionService encryptionService) : IValueConverter<int, string>
    {
        public string Convert(int sourceMember, ResolutionContext context) => encryptionService.Encrypt(sourceMember.ToString());
    }
}