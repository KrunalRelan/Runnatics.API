using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.About;
using Runnatics.Models.Client.Responses.About;

namespace Runnatics.Services.Interface
{
    public interface IAboutContentService : ISimpleServiceBase
    {
        /// <summary>Public About page payload (texts + active founders in display order).</summary>
        Task<PublicAboutDto> GetPublicAboutAsync(CancellationToken ct = default);

        /// <summary>Admin editor view — same content plus encrypted founder ids.</summary>
        Task<AboutContentDto> GetAboutContentAsync(CancellationToken ct = default);

        /// <summary>Upserts the About copy rows (full-state PUT).</summary>
        Task<bool> UpdateAboutContentAsync(UpdateAboutContentRequest request, CancellationToken ct = default);

        Task<FounderDto?> CreateFounderAsync(SaveFounderRequest request, CancellationToken ct = default);

        Task<FounderDto?> UpdateFounderAsync(string encryptedFounderId, SaveFounderRequest request, CancellationToken ct = default);

        Task<bool> DeleteFounderAsync(string encryptedFounderId, CancellationToken ct = default);
    }
}
