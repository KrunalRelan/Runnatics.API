using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public class IdListEncryptor(IEncryptionService encryptionService) : IValueConverter<List<int>, List<string>>
    {
        public List<string> Convert(List<int> sourceMember, ResolutionContext context)
        {
            var idIncryptor = new IdEncryptor(encryptionService);
            return [.. sourceMember.Select(id => idIncryptor.Convert(id, context))];
        }
    }
}