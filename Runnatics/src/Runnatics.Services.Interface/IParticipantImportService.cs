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

        Task<PagingList<ParticipantSearchReponse>> Search(ParticipantSearchRequest request, string eventId, string raceId);

        Task AddParticipant(string eventId, string raceId, ParticipantRequest addParticipant);

        Task EditParticipant(string participantId, ParticipantRequest editParticipant);

        Task DeleteParicipant(string participantId);
    }
}