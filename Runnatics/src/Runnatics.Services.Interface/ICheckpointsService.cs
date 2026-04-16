using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.CheckPoints;
using Runnatics.Models.Client.Responses.Checkpoints;

namespace Runnatics.Services.Interface
{
    public interface ICheckpointsService : ISimpleServiceBase
    {
        Task<PagingList<CheckpointResponse>> Search(string eventId, string raceId);

        Task<bool> Create(string eventId, string raceId, CheckpointRequest request);

        Task<bool> BulkCreate(string eventId, string raceId, List<CheckpointRequest> request);

        Task<bool> Update(string eventId, string raceId, string checkpointId, CheckpointRequest request);

        Task<bool> Delete(string eventId, string raceId, string checkpointId);
        
        Task<bool> DeleteAll(string eventId, string raceId);

        Task<CheckpointResponse> GetCheckpoint(string eventId, string raceId, string checkpointId);
        
        Task<bool> Clone(string eventId, string sourceRaceId, string destinationRaceId);

        /// <summary>
        /// Generates loop checkpoints for a race based on RaceSettings.HasLoops and LoopLength.
        /// Clones start-line devices at each loop interval up to the full race distance.
        /// The checkpoint at the full distance is named "Finish".
        /// </summary>
        Task<bool> AddLoops(string eventId, string raceId);
    }
}
