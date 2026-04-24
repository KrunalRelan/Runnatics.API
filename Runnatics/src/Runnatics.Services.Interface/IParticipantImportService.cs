using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Client.Responses.Participants;

namespace Runnatics.Services.Interface
{
    public interface IParticipantImportService : ISimpleServiceBase
    {
        Task<ParticipantImportResponse> UploadParticipantsCsvAsync(
           string eventId,
           ParticipantImportRequest request);

        /// <summary>
        /// Process staging data and move to main Participants table
        /// </summary>
        Task<ProcessImportResponse> ProcessStagingDataAsync(ProcessImportRequest request);

        /// <summary>
        /// Updates existing participants by matching bib numbers from uploaded CSV.
        /// Used when participants were created via AddParticipantRange with only bib numbers,
        /// and now need to be updated with full details (name, email, etc.)
        /// </summary>
        Task<UpdateParticipantsByBibResponse> UpdateParticipantsByBibAsync(
            string eventId,
            string raceId,
            UpdateParticipantsByBibRequest request);

        Task<PagingList<ParticipantSearchReponse>> Search(ParticipantSearchRequest request, string eventId, string raceId);

        Task AddParticipant(string eventId, string raceId, ParticipantRequest addParticipant);

        Task EditParticipant(string participantId, ParticipantRequest editParticipant);

        Task DeleteParicipant(string participantId);

        /// <summary>
        /// Extended update for admin use: supports RunStatus, DisqualificationReason, ManualTime, ManualDistance, LoopCount, and race reassignment.
        /// </summary>
        Task<ParticipantSearchReponse?> UpdateParticipantExtendedAsync(string raceId, string participantId, UpdateParticipantRequest request);

        /// <summary>
        /// Soft-deletes a participant, verifying they belong to the given race.
        /// </summary>
        Task DeleteParticipantAsync(string raceId, string participantId);

        Task<List<Category>> GetCategories(string eventId, string raceId);

        Task<AddParticipantRangeResponse> AddParticipantRangeAsync(string eventId, string raceId, AddParticipantRangeRequest request);

        /// <summary>
        /// Get detailed participant information including performance, rankings, split times and pace progression
        /// </summary>
        Task<ParticipantDetailsResponse?> GetParticipantDetails(string eventId, string raceId, string participantId);

        /// <summary>
        /// Export all participants for a race as an xlsx file with dynamic checkpoint columns.
        /// </summary>
        Task<byte[]?> ExportParticipantsAsync(string raceId);

        /// <summary>
        /// Export all participants with full race details: ranking, chip/gun times, SMS sent time, and absolute checkpoint clock times.
        /// </summary>
        Task<byte[]?> ExportParticipantsDetailedAsync(string eventId, string raceId);
    }
}