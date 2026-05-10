using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Public;

namespace Runnatics.Services.Interface
{
    public interface IPublicResultsService : ISimpleServiceBase
    {
        Task<PublicResultsResponseDto?> GetPublicEventResultsAsync(
            string encryptedEventId,
            GetPublicEventResultsRequest request,
            CancellationToken ct = default);

        Task<PublicResultDto?> GetPublicResultByBibAsync(
            string encryptedEventId,
            string bib,
            CancellationToken ct = default);

        Task<PublicGroupedLeaderboardDto?> GetPublicGroupedLeaderboardAsync(
            string eventId,
            string raceId,
            GetPublicLeaderboardRequest request,
            CancellationToken ct = default);

        Task<PublicParticipantDetailDto?> GetPublicParticipantDetailAsync(
            string participantId,
            CancellationToken ct = default);

        Task<PublicResultFiltersDto> GetResultFiltersAsync(CancellationToken ct = default);

        Task<PublicRaceFilterDto?> GetRaceFiltersAsync(
            string encryptedEventId,
            CancellationToken ct = default);

        Task<PublicBracketFilterDto?> GetBracketFiltersAsync(
            string encryptedEventId,
            string encryptedRaceId,
            CancellationToken ct = default);

        Task<List<PublicParticipantSearchResultDto>> SearchParticipantsForComparisonAsync(
            SearchParticipantsRequest request,
            CancellationToken ct = default);

        Task<PublicParticipantComparisonDto?> CompareParticipantsAsync(
            CompareParticipantsRequest request,
            CancellationToken ct = default);

        Task<byte[]?> GetPublicParticipantCertificateAsync(
            string encryptedParticipantId,
            CancellationToken ct = default);
    }
}
