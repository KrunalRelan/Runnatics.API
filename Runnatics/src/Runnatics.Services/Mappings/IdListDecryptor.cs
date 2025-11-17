using AutoMapper;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Mappings
{
    public class IdListDecryptor(IEncryptionService encryptionService) : IValueConverter<List<string>, List<int>>
    {
        public List<int> Convert(List<string> sourceMember, ResolutionContext context)
        {
            var idDecryptor = new IdDecryptor(encryptionService);
            return [.. sourceMember.Select(id => idDecryptor.Convert(id, context))];
        }
    }
}