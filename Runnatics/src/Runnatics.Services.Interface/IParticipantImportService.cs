using Microsoft.AspNetCore.Http;
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
        Task<ProcessImportResponse> ProcessStagingDataAsync(
            string eventId,
            string importBatchId,
            ProcessImportRequest request);
    }

}