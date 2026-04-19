using Runnatics.Models.Client.Requests.BibMapping;
using Runnatics.Models.Client.Responses.BibMapping;

namespace Runnatics.Services.Interface
{
    public interface IBibMappingService : ISimpleServiceBase
    {
        Task<CreateBibMappingServiceResult> CreateAsync(CreateBibMappingRequest request, CancellationToken cancellationToken = default);

        Task<List<BibMappingResponse>> GetByRaceAsync(string encryptedRaceId, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string encryptedChipId, string encryptedParticipantId, string encryptedEventId, CancellationToken cancellationToken = default);
    }
}
