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
                // Decrypt IDs passed from controller
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

                // Validate parent Event exists
                var eventRepo = _repository.GetRepository<Event>();
                var parentEvent = await eventRepo.GetQuery(e =>
                        e.Id == decryptedEventId &&
                        e.AuditProperties.IsActive &&
                        !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (parentEvent == null)
                {
                    ErrorMessage = "Event not found or inactive.";
                    _logger.LogWarning("Checkpoint creation aborted - event not found. EventId: {EventId}", decryptedEventId);
                    return false;
                }

                // Validate Race exists and belongs to Event
                var raceRepo = _repository.GetRepository<Race>();
                var parentRace = await raceRepo.GetQuery(r =>
                        r.Id == decryptedRaceId &&
                        r.EventId == decryptedEventId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (parentRace == null)
                {
                    ErrorMessage = "Race not found or inactive.";
                    _logger.LogWarning("Checkpoint creation aborted - race not found. EventId: {EventId}, RaceId: {RaceId}", decryptedEventId, decryptedRaceId);
                    return false;
                }

                // Map request to entity; include decrypted EventId/RaceId and current user id in mapping context
                var checkpoint = _mapper.Map<Checkpoint>(request, opts =>
                {
                    opts.Items["UserId"] = currentUserId;
                    opts.Items["EventId"] = decryptedEventId;
                    opts.Items["RaceId"] = decryptedRaceId;
                });

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

        public Task<bool> Delete(string eventId, string raceId, string checkpointId)
        {
            return DeleteAsync(eventId, raceId, checkpointId);
        }

        private async Task<bool> DeleteAsync(string eventId, string raceId, string checkpointId)
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
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (checkpoint == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint with id {CheckpointId} not found for Event {EventId} Race {RaceId}", decryptedCheckpointId, decryptedEventId, decryptedRaceId);
                    return null!;
                }

                // Map to response DTO
                var response = new CheckpointResponse
                {
                    Id = checkpoint.Id,
                    EventId = checkpoint.EventId,
                    RaceId = checkpoint.RaceId,
                    Name = checkpoint.Name,
                    DistanceFromStart = checkpoint.DistanceFromStart,
                    DeviceId = checkpoint.DeviceId,
                    ParentDeviceId = checkpoint.ParentDeviceId,
                    IsMandatory = checkpoint.IsMandatory
                };

                return response;
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
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var query = checkpointRepo.GetQuery(c =>
                        c.EventId == decryptedEventId &&
                        c.RaceId == decryptedRaceId &&
                        c.AuditProperties.IsActive &&
                        !c.AuditProperties.IsDeleted)
                    .AsNoTracking();

                var list = await query
                    .OrderBy(c => c.Id)
                    //.ThenBy(c => c.DistanceFromStart)
                    .ToListAsync();

                var responses = list.Select(c => new CheckpointResponse
                {
                    Id = c.Id,
                    EventId = c.EventId,
                    RaceId = c.RaceId,
                    Name = c.Name,
                    DistanceFromStart = c.DistanceFromStart,
                    DeviceId = c.DeviceId,
                    ParentDeviceId = c.ParentDeviceId,
                    IsMandatory = c.IsMandatory
                }).ToList();

                var paging = new PagingList<CheckpointResponse>(responses)
                {
                    TotalCount = responses.Count
                };

                return paging;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching checkpoints for EventId: {EventId}, RaceId: {RaceId}", eventId, raceId);
                ErrorMessage = "Error retrieving checkpoints.";
                return [];
            }
        }

        public async Task<bool> Update(string eventId, string raceId, string checkpointId, CheckpointRequest request)
        {
            try
            {
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedCheckpointId = Convert.ToInt32(_encryptionService.Decrypt(checkpointId));
                var decryptedDeviceId = Convert.ToInt32(_encryptionService.Decrypt(request.DeviceId));
                var decryptedParentDeviceId = request.ParentDeviceId != null
                    ? Convert.ToInt32(_encryptionService.Decrypt(request.ParentDeviceId))
                    : (int?)null;

                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var existing = await checkpointRepo.GetQuery(c =>
                        c.Id == decryptedCheckpointId &&
                        c.EventId == decryptedEventId &&
                        c.RaceId == decryptedRaceId &&
                        c.AuditProperties.IsActive &&
                        !c.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint update failed - not found. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", decryptedEventId, decryptedRaceId, decryptedCheckpointId);
                    return false;
                }

                // Map allowed fields from request to entity (do not overwrite audit/ids)
                existing.Name = request.Name;
                existing.DistanceFromStart = request.DistanceFromStart;
                existing.DeviceId = decryptedDeviceId;
                existing.ParentDeviceId = decryptedParentDeviceId;

                // Update audit
                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existing.AuditProperties.UpdatedBy = _userContext.UserId;

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

    }
}
