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

        Task<List<Category>> GetCategories(string eventId, string raceId);

        Task<AddParticipantRangeResponse> AddParticipantRangeAsync(string eventId, string raceId, AddParticipantRangeRequest request);
    }
}