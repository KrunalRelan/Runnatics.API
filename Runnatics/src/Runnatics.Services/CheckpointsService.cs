using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.CheckPoints;
using Runnatics.Models.Client.Responses.Checkpoints;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class CheckpointService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<CheckpointService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), ICheckpointsService
    {
        protected readonly IMapper _mapper = mapper;
        private readonly ILogger<CheckpointService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;

        public async Task<bool> Create(string eventId, string raceId, CheckpointRequest request)
        {
            try
            {
                var (decryptedEventId, decryptedRaceId) = DecryptEventAndRace(eventId, raceId);
                //var (decryptedDeviceId, decryptedParentDeviceId) = DecryptDeviceAndParentDevice(request.DeviceId, request.ParentDeviceId ?? "0");

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

                if (!await ParentEventAndRaceExistAsync(decryptedEventId, decryptedRaceId))
                {
                    return false; 
                }

                // Map request to entity; include decrypted EventId/RaceId and current user id in mapping context
                var checkpoint = _mapper.Map<Checkpoint>(request);

                checkpoint.Name = request.Name;
                checkpoint.EventId = decryptedEventId;
                checkpoint.RaceId = decryptedRaceId;
                checkpoint.IsMandatory = request.IsMandatory;
                checkpoint.DistanceFromStart = request.DistanceFromStart;
                //checkpoint.ParentDeviceId = 1; //decryptedParentDeviceId;
                checkpoint.DeviceId = 1; // decryptedDeviceId;
                checkpoint.AuditProperties = CreateAuditProperties(currentUserId);

                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                await checkpointRepo.AddAsync(checkpoint);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Checkpoint created successfully. Id: {Id}, EventId: {EventId}, RaceId: {RaceId}, CreatedBy: {UserId}", checkpoint.Id, checkpoint.EventId, checkpoint.RaceId, currentUserId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating checkpoint for EventId: {EventId} RaceId: {RaceId}", eventId, raceId);
                ErrorMessage = "Error creating checkpoint.";
                return false;
            }
        }

        public async Task<bool> Delete(string eventId, string raceId, string checkpointId)
        {
            try
            {
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedCheckpointId = Convert.ToInt32(_encryptionService.Decrypt(checkpointId));

                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var checkpoint = await checkpointRepo.GetQuery(c =>
                        c.Id == decryptedCheckpointId &&
                        c.EventId == decryptedEventId &&
                        c.RaceId == decryptedRaceId &&
                        c.AuditProperties.IsActive &&
                        !c.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (checkpoint == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint delete failed - not found. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", decryptedEventId, decryptedRaceId, decryptedCheckpointId);
                    return false;
                }

                // Soft delete
                checkpoint.AuditProperties.IsActive = false;
                checkpoint.AuditProperties.IsDeleted = true;
                checkpoint.AuditProperties.UpdatedDate = DateTime.UtcNow;
                checkpoint.AuditProperties.UpdatedBy = _userContext.UserId;

                await checkpointRepo.UpdateAsync(checkpoint);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Checkpoint deleted successfully. Id: {CheckpointId}, EventId: {EventId}, RaceId: {RaceId}", decryptedCheckpointId, decryptedEventId, decryptedRaceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting checkpoint. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", eventId, raceId, checkpointId);
                ErrorMessage = "Error deleting checkpoint.";
                return false;
            }
        }

        public async Task<CheckpointResponse> GetCheckpoint(string eventId, string raceId, string checkpointId)
        {
            try
            {
                var (decryptedEventId, decryptedRaceId, decryptedCheckpointId) = DecryptAll(eventId, raceId, checkpointId);
                var checkpoint = await FindCheckpointAsync(decryptedCheckpointId, decryptedEventId, decryptedRaceId);

                if (checkpoint == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint with id {CheckpointId} not found for Event {EventId} Race {RaceId}", decryptedCheckpointId, decryptedEventId, decryptedRaceId);
                    return null!;
                }

                return _mapper.Map<CheckpointResponse>(checkpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving checkpoint. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", eventId, raceId, checkpointId);
                ErrorMessage = "Error retrieving checkpoint.";
                return null!;
            }
        }

        public async Task<PagingList<CheckpointResponse>> Search(string eventId, string raceId)
        {
            try
            {
                var (decryptedEventId, decryptedRaceId) = DecryptEventAndRace(eventId, raceId);
                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var list = await checkpointRepo.GetQuery(c =>
                        c.EventId == decryptedEventId &&
                        c.RaceId == decryptedRaceId &&
                        c.AuditProperties.IsActive &&
                        !c.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .OrderBy(c => c.Id)
                    .ThenBy(c => c.DistanceFromStart)
                    .ToListAsync();

                var responses = _mapper.Map<List<CheckpointResponse>>(list);
                return new PagingList<CheckpointResponse>(responses) { TotalCount = responses.Count };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching checkpoints for EventId: {EventId}, RaceId: {RaceId}", eventId, raceId);
                ErrorMessage = "Error retrieving checkpoints.";
                return new PagingList<CheckpointResponse>();
            }
        }

        public async Task<bool> Update(string eventId, string raceId, string checkpointId, CheckpointRequest request)
        {
            try
            {
                var (decryptedEventId, decryptedRaceId, decryptedCheckpointId) = DecryptAll(eventId, raceId, checkpointId);

                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var existing = await FindCheckpointAsync(decryptedCheckpointId, decryptedEventId, decryptedRaceId);
                if (existing == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint update failed - not found. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", decryptedEventId, decryptedRaceId, decryptedCheckpointId);
                    return false;
                }

                // Map allowed fields (uses AutoMapper mapping with SetUpdated)
                _mapper.Map(request, existing, opts =>
                {
                    opts.Items["SetUpdated"] = true;
                    opts.Items["UserId"] = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;
                });

                await checkpointRepo.UpdateAsync(existing);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Checkpoint updated successfully. Id: {CheckpointId}, EventId: {EventId}, RaceId: {RaceId}", decryptedCheckpointId, decryptedEventId, decryptedRaceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating checkpoint. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", eventId, raceId, checkpointId);
                ErrorMessage = "Error updating checkpoint.";
                return false;
            }
        }

        #region Helpers

        private (int eventId, int raceId) DecryptEventAndRace(string eventId, string raceId)
        {
            return (Convert.ToInt32(_encryptionService.Decrypt(eventId)), Convert.ToInt32(_encryptionService.Decrypt(raceId)));
        }

        private (int deviceId, int parentDeviceId) DecryptDeviceAndParentDevice(string deviceId, string parentDeviceId)
        {
            return (Convert.ToInt32(_encryptionService.Decrypt(deviceId)), Convert.ToInt32(_encryptionService.Decrypt(parentDeviceId)));
        }

        private (int eventId, int raceId, int checkpointId) DecryptAll(string eventId, string raceId, string checkpointId)
        {
            return (Convert.ToInt32(_encryptionService.Decrypt(eventId)), Convert.ToInt32(_encryptionService.Decrypt(raceId)), Convert.ToInt32(_encryptionService.Decrypt(checkpointId)));
        }

        private async Task<bool> ParentEventAndRaceExistAsync(int eventId, int raceId)
        {
            var eventRepo = _repository.GetRepository<Event>();
            var parentEvent = await eventRepo.GetQuery(e => e.Id == eventId && e.AuditProperties.IsActive && !e.AuditProperties.IsDeleted)
                .AsNoTracking().FirstOrDefaultAsync();
            if (parentEvent == null)
            {
                ErrorMessage = "Event not found or inactive.";
                _logger.LogWarning("Parent event not found: {EventId}", eventId);
                return false;
            }

            var raceRepo = _repository.GetRepository<Race>();
            var parentRace = await raceRepo.GetQuery(r => r.Id == raceId && r.EventId == eventId && r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .AsNoTracking().FirstOrDefaultAsync();
            if (parentRace == null)
            {
                ErrorMessage = "Race not found or inactive.";
                _logger.LogWarning("Parent race not found: EventId={EventId}, RaceId={RaceId}", eventId, raceId);
                return false;
            }

            return true;
        }

        private Task<Checkpoint?> FindCheckpointAsync(int checkpointId, int eventId, int raceId)
        {
            var repo = _repository.GetRepository<Checkpoint>();
            return repo.GetQuery(c => c.Id == checkpointId && c.EventId == eventId && c.RaceId == raceId && c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Creates audit properties for an entity
        /// </summary>
        private static Models.Data.Common.AuditProperties CreateAuditProperties(int userId)
        {
            return new Models.Data.Common.AuditProperties
            {
                IsActive = true,
                IsDeleted = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = userId
            };
        }
        #endregion

    }
}
