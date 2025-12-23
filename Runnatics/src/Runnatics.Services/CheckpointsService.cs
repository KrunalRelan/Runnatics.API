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
                var (decryptedDeviceId, decryptedParentDeviceId) = DecryptDeviceAndParentDevice(request.DeviceId, request.ParentDeviceId);

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

                if (!await ParentEventAndRaceExistAsync(decryptedEventId, decryptedRaceId))
                {
                    return false;
                }

                if (decryptedDeviceId.HasValue && !await DeviceExistsAsync(decryptedDeviceId.Value))
                {
                    ErrorMessage = "Device not found.";
                    return false;
                }

                if (decryptedParentDeviceId.HasValue && !await DeviceExistsAsync(decryptedParentDeviceId.Value))
                {
                    ErrorMessage = "Parent device not found.";
                    return false;
                }

                // Map request to entity
                var checkpoint = _mapper.Map<Checkpoint>(request);

                checkpoint.EventId = decryptedEventId;
                checkpoint.RaceId = decryptedRaceId;
                checkpoint.DeviceId = decryptedDeviceId ?? 0;
                checkpoint.ParentDeviceId = decryptedParentDeviceId ?? 0;
                checkpoint.Name = request.Name;
                checkpoint.IsMandatory = request.IsMandatory;
                checkpoint.DistanceFromStart = request.DistanceFromStart;
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

        public async Task<bool> BulkCreate(string eventId, string raceId, List<CheckpointRequest> requests)
        {
            try
            {
                if (requests == null || requests.Count == 0)
                {
                    ErrorMessage = "No checkpoints provided.";
                    return false;
                }

                var (decryptedEventId, decryptedRaceId) = DecryptEventAndRace(eventId, raceId);

                if (!await ParentEventAndRaceExistAsync(decryptedEventId, decryptedRaceId))
                {
                    return false;
                }

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;
                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var entities = new List<Checkpoint>(requests.Count);

                foreach (var req in requests)
                {
                    // FIX: Changed to use nullable return type for devices
                    var (decryptedDeviceId, decryptedParentDeviceId) = DecryptDeviceAndParentDevice(req.DeviceId, req.ParentDeviceId);

                    var checkpoint = _mapper.Map<Checkpoint>(req);

                    checkpoint.EventId = decryptedEventId;
                    checkpoint.RaceId = decryptedRaceId;
                    checkpoint.DeviceId = decryptedDeviceId ?? 0;
                    checkpoint.ParentDeviceId = decryptedParentDeviceId;
                    checkpoint.Name = req.Name;
                    checkpoint.IsMandatory = req.IsMandatory;
                    checkpoint.DistanceFromStart = req.DistanceFromStart;
                    checkpoint.AuditProperties = CreateAuditProperties(currentUserId);

                    entities.Add(checkpoint);
                }

                await checkpointRepo.AddRangeAsync(entities);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Bulk created {Count} checkpoints for EventId: {EventId}, RaceId: {RaceId} by UserId: {UserId}", entities.Count, decryptedEventId, decryptedRaceId, currentUserId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk creating checkpoints for EventId: {EventId}, RaceId: {RaceId}", eventId, raceId);
                ErrorMessage = "Error creating checkpoints.";
                return false;
            }
        }

        public async Task<bool> Clone(string eventId, string sourceRaceId, string destinationRaceId)
        {
            try
            {
                var (decryptedEventId, decryptedSourceRaceId) = DecryptEventAndRace(eventId, sourceRaceId);
                // FIX: Using TryParseOrDecrypt instead of Convert.ToInt32 for consistency
                var decryptedDestinationRaceId = TryParseOrDecrypt(destinationRaceId, nameof(destinationRaceId));

                // verify event and both races exist
                if (!await ParentEventAndRaceExistAsync(decryptedEventId, decryptedSourceRaceId))
                {
                    return false;
                }

                if (!await ParentEventAndRaceExistAsync(decryptedEventId, decryptedDestinationRaceId))
                {
                    return false;
                }

                var checkpointRepo = _repository.GetRepository<Checkpoint>();

                var sourceCheckpoints = await checkpointRepo.GetQuery(c => c.EventId == decryptedEventId && c.RaceId == decryptedSourceRaceId && c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .OrderBy(c => c.DistanceFromStart)
                    .ToListAsync();

                if (sourceCheckpoints == null || sourceCheckpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints to clone from source race.";
                    return false;
                }

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

                var clones = sourceCheckpoints
                    .Select(src => new Checkpoint
                    {
                        EventId = src.EventId,
                        RaceId = decryptedDestinationRaceId,
                        Name = src.Name,
                        DistanceFromStart = src.DistanceFromStart,
                        DeviceId = src.DeviceId,
                        ParentDeviceId = src.ParentDeviceId,
                        IsMandatory = src.IsMandatory,
                        AuditProperties = CreateAuditProperties(currentUserId)
                    })
                    .ToList();

                await checkpointRepo.AddRangeAsync(clones);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Cloned {Count} checkpoints from Race {SrcRace} to Race {DstRace} for Event {EventId}", clones.Count, decryptedSourceRaceId, decryptedDestinationRaceId, decryptedEventId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cloning checkpoints. Event: {EventId}, SourceRace: {Src}, DestinationRace: {Dst}", eventId, sourceRaceId, destinationRaceId);
                ErrorMessage = "Error cloning checkpoints.";
                return false;
            }
        }

        public async Task<bool> Delete(string eventId, string raceId, string checkpointId)
        {
            try
            {
                // FIX: Using TryParseOrDecrypt for consistency
                var decryptedEventId = TryParseOrDecrypt(eventId, nameof(eventId));
                var decryptedRaceId = TryParseOrDecrypt(raceId, nameof(raceId));
                var decryptedCheckpointId = TryParseOrDecrypt(checkpointId, nameof(checkpointId));

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
                // FIX: Include Device navigation properties when fetching single checkpoint
                var checkpoint = await FindCheckpointWithDevicesAsync(decryptedCheckpointId, decryptedEventId, decryptedRaceId);

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
                    .Include(c => c.Device)
                    .Include(c => c.ParentDevice)
                    .OrderBy(c => c.DistanceFromStart)
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
                // FIX: Changed to use nullable return type for devices
                var (decryptedDeviceId, decryptedParentDeviceId) = DecryptDeviceAndParentDevice(request.DeviceId, request.ParentDeviceId);

                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var existing = await FindCheckpointAsync(decryptedCheckpointId, decryptedEventId, decryptedRaceId);

                if (existing == null)
                {
                    ErrorMessage = "Checkpoint not found.";
                    _logger.LogWarning("Checkpoint update failed - not found. EventId: {EventId}, RaceId: {RaceId}, CheckpointId: {CheckpointId}", decryptedEventId, decryptedRaceId, decryptedCheckpointId);
                    return false;
                }

                // FIX: Validate device exists if provided
                if (decryptedDeviceId.HasValue && !await DeviceExistsAsync(decryptedDeviceId.Value))
                {
                    ErrorMessage = "Device not found.";
                    return false;
                }

                // FIX: Validate parent device exists if provided
                if (decryptedParentDeviceId.HasValue && !await DeviceExistsAsync(decryptedParentDeviceId.Value))
                {
                    ErrorMessage = "Parent device not found.";
                    return false;
                }

                var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

                // FIX: Manually update all fields instead of using AutoMapper
                // AutoMapper was configured to ignore DeviceId and ParentDeviceId, causing updates to fail
                existing.Name = request.Name;
                existing.DistanceFromStart = request.DistanceFromStart;
                existing.IsMandatory = request.IsMandatory;
                existing.DeviceId = decryptedDeviceId  ?? 0;
                existing.ParentDeviceId = decryptedParentDeviceId ?? 0;
                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existing.AuditProperties.UpdatedBy = currentUserId;

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
            return (TryParseOrDecrypt(eventId, nameof(eventId)), TryParseOrDecrypt(raceId, nameof(raceId)));
        }

        /// <summary>
        /// Decrypts device and parent device IDs. Returns nullable ints since devices are optional.
        /// FIX: Changed return type from (int, int?) to (int?, int?) since both devices are optional
        /// </summary>
        private (int? deviceId, int? parentDeviceId) DecryptDeviceAndParentDevice(string? deviceId, string? parentDeviceId)
        {
            int? decryptedDeviceId = null;
            int? decryptedParentDeviceId = null;

            if (!string.IsNullOrEmpty(deviceId))
            {
                decryptedDeviceId = TryParseOrDecrypt(deviceId, nameof(deviceId));
            }

            if (!string.IsNullOrEmpty(parentDeviceId))
            {
                decryptedParentDeviceId = TryParseOrDecrypt(parentDeviceId, nameof(parentDeviceId));
            }

            return (decryptedDeviceId, decryptedParentDeviceId);
        }

        private (int eventId, int raceId, int checkpointId) DecryptAll(string eventId, string raceId, string checkpointId)
        {
            return (TryParseOrDecrypt(eventId, nameof(eventId)), TryParseOrDecrypt(raceId, nameof(raceId)), TryParseOrDecrypt(checkpointId, nameof(checkpointId)));
        }

        /// <summary>
        /// Attempts to parse the input as an integer. If parsing fails, attempts to decrypt and parse the result.
        /// Throws ArgumentException when neither parsing nor decryption produce a valid integer.
        /// </summary>
        private int TryParseOrDecrypt(string input, string inputName)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Id input cannot be null or empty", inputName);

            if (int.TryParse(input, out var id))
                return id;

            try
            {
                var decrypted = _encryptionService.Decrypt(input);
                if (int.TryParse(decrypted, out id))
                    return id;

                _logger.LogDebug("Decrypted value for {InputName} did not parse as int", inputName);
                throw new ArgumentException($"Invalid {inputName} format");
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                // log debug for diagnostics but avoid logging sensitive material
                _logger.LogDebug(ex, "Failed to parse or decrypt input for {InputName}", inputName);
                throw new ArgumentException($"Invalid {inputName} format", inputName, ex);
            }
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

        /// <summary>
        /// FIX: Added helper method to check if a device exists
        /// </summary>
        private async Task<bool> DeviceExistsAsync(int deviceId)
        {
            var deviceRepo = _repository.GetRepository<Device>();
            return await deviceRepo.GetQuery(d => d.Id == deviceId && d.AuditProperties.IsActive && !d.AuditProperties.IsDeleted)
                .AsNoTracking()
                .AnyAsync();
        }

        private Task<Checkpoint?> FindCheckpointAsync(int checkpointId, int eventId, int raceId)
        {
            var repo = _repository.GetRepository<Checkpoint>();
            return repo.GetQuery(c => c.Id == checkpointId && c.EventId == eventId && c.RaceId == raceId && c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// FIX: Added helper method to fetch checkpoint with device navigation properties
        /// </summary>
        private Task<Checkpoint?> FindCheckpointWithDevicesAsync(int checkpointId, int eventId, int raceId)
        {
            var repo = _repository.GetRepository<Checkpoint>();
            return repo.GetQuery(c => c.Id == checkpointId && c.EventId == eventId && c.RaceId == raceId && c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                .Include(c => c.Device)
                .Include(c => c.ParentDevice)
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