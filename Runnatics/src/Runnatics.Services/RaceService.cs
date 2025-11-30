using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Linq.Expressions;


namespace Runnatics.Services
{
    public class RaceService(IUnitOfWork<RaceSyncDbContext> repository,
                               IMapper mapper,
                               ILogger<RaceService> logger,
                               IConfiguration configuration,
                               IUserContextService userContext,
                               IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IRacesService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger<RaceService> _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;
        protected readonly IEncryptionService _encryptionService = encryptionService;


        public async Task<Models.Client.Common.PagingList<RaceResponse>> Search(string eId, RaceSearchRequest request)
        {
            try
            {
                var eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));

                // Validate date range
                if (!ValidateDateRange(request))
                {
                    return [];
                }

                // Build and execute search query
                var searchResults = await ExecuteSearchQueryAsync(eventId, request);

                // Project to DTOs — include race-level leaderboard settings or fallback to event-level in loader
                var raceResponses = await LoadRaceResponsesAsync(searchResults);

                // Create paging result
                var paging = new Models.Client.Common.PagingList<RaceResponse>(raceResponses)
                {
                    TotalCount = searchResults.TotalCount
                };

                _logger.LogInformation("Race search completed for Event {EventId} by User {UserId}. Found {Count} races.",
               eventId, _userContext.UserId, paging.TotalCount);

                return paging;
            }
            catch (UnauthorizedAccessException ex)
            {
                this.ErrorMessage = "Unauthorized: " + ex.Message;
                _logger.LogWarning(ex, "Unauthorized race search attempt");
                return [];
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error during event search");
                return [];
            }
        }

        // RaceService.cs - Complete Create Method

        public async Task<bool> Create(string eId, RaceRequest request)
        {
            if (!ValidateRaceRequest(request))
            {
                return false;
            }

            var eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));
            var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                var parentEvent = await eventRepo.GetQuery(e =>
                        e.Id == eventId &&
                        e.AuditProperties.IsActive &&
                        !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (parentEvent == null)
                {
                    ErrorMessage = "Event not found or inactive.";
                    _logger.LogWarning("Race creation aborted - event not found. EventId: {EventId}", eventId);
                    return false;
                }

                await _repository.BeginTransactionAsync();

                try
                {
                    // Map race basic properties
                    var race = _mapper.Map<Race>(request);
                    race.EventId = eventId;
                    race.AuditProperties = CreateAuditProperties(currentUserId);

                    // Handle RaceSettings
                    if (request.RaceSettings != null)
                    {
                        race.RaceSettings = CreateRaceSettings(request.RaceSettings, currentUserId);
                    }

                    // Handle LeaderboardSettings - Create ONLY if override is enabled
                    if (request.LeaderboardSettings != null)
                    {
                        race.LeaderboardSettings = _mapper.Map<LeaderboardSettings>(request.LeaderboardSettings);
                        race.LeaderboardSettings.EventId = eventId;
                        race.LeaderboardSettings.OverrideSettings = request.OverrideSettings ?? false;
                        race.LeaderboardSettings.AuditProperties = CreateAuditProperties(currentUserId);
                    }
                    // If request.LeaderboardSettings is null, race.LeaderboardSettings stays null
                    // This means the race will use event-level settings (fallback)

                    // Add race to repository
                    var raceRepo = _repository.GetRepository<Race>();
                    await raceRepo.AddAsync(race);

                    // Save everything in one transaction
                    // EF Core will:
                    // 1. Insert Race record
                    // 2. Automatically insert LeaderboardSettings (if not null) with correct RaceId
                    await _repository.SaveChangesAsync();

                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation(
                        "Race created successfully. RaceId: {RaceId}, EventId: {EventId}, HasCustomLeaderboard: {HasCustom}, CreatedBy: {UserId}",
                        race.Id,
                        race.EventId,
                        race.LeaderboardSettings != null,
                        currentUserId);

                    return true;
                }
                catch (Exception exInner)
                {
                    await _repository.RollbackTransactionAsync();
                    _logger.LogError(exInner, "Error creating race for EventId: {EventId}. Error: {Error}",
                        eventId, exInner.Message);
                    ErrorMessage = "Error creating race.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating race for EventId: {EventId}", eventId);
                ErrorMessage = "An unexpected error occurred while creating the race.";
                return false;
            }
        }

        public async Task<RaceResponse?> GetRaceById(string eId, string id)
        {
            try
            {
                var eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));
                var raceId = Convert.ToInt32(_encryptionService.Decrypt(id));

                var raceRepo = _repository.GetRepository<Race>();

                var raceDb = await raceRepo.GetQuery(e =>
                        e.Id == raceId &&
                        e.EventId == eventId &&
                        e.AuditProperties.IsActive &&
                        !e.AuditProperties.IsDeleted)
                    .Include(e => e.RaceSettings)
                    .Include(e => e.Event)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (raceDb == null)
                {
                    this.ErrorMessage = "Race not found.";
                    _logger.LogWarning("Race with ID {RaceId} not found.", id);
                    return null;
                }

                // Get effective leaderboard settings
                var effectiveLeaderboardSettings = await GetEffectiveLeaderboardSettings(eventId, raceId);

                // Map race to response
                var raceResponse = _mapper.Map<RaceResponse>(raceDb);
                raceResponse.LeaderboardSettings = effectiveLeaderboardSettings;

                return raceResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving race : {Id}", id);
                this.ErrorMessage = "Error retrieving race.";
                return null;
            }
        }

        /// <summary>
        /// Gets effective leaderboard settings for a race
        /// If race has override settings (OverrideSettings = true), return race-level settings
        /// Otherwise, return event-level settings (RaceId = NULL)
        /// </summary>
        private async Task<LeaderboardSettings?> GetEffectiveLeaderboardSettings(int eventId, int raceId)
        {
            var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();

            // First, check if race has override settings
            var raceLeaderboardSettings = await leaderboardRepo
                .GetQuery(lb =>
                    lb.EventId == eventId &&
                    lb.RaceId == raceId &&
                    lb.OverrideSettings == true &&
                    lb.AuditProperties.IsActive &&
                    !lb.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (raceLeaderboardSettings != null)
            {
                _logger.LogInformation("Using race-level leaderboard settings for RaceId: {RaceId}", raceId);
                return raceLeaderboardSettings;
            }

            // Fall back to event-level settings
            var eventLeaderboardSettings = await leaderboardRepo
                .GetQuery(lb =>
                    lb.EventId == eventId &&
                    lb.RaceId == null &&
                    lb.OverrideSettings == false &&
                    lb.AuditProperties.IsActive &&
                    !lb.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (eventLeaderboardSettings != null)
            {
                _logger.LogInformation("Using event-level leaderboard settings for RaceId: {RaceId}", raceId);
            }

            return eventLeaderboardSettings;
        }

        public async Task<bool> Update(string eventId, string id, RaceRequest request)
        {
            try
            {
                var currentUserId = _userContext.UserId;

                if (!ValidateRaceRequest(request))
                {
                    return false;
                }

                var raceEntity = await GetRaceForUpdateAsync(id, eventId);
                if (raceEntity == null)
                {
                    this.ErrorMessage = $"Race with ID {id} not found or you don't have permission to update it.";
                    _logger.LogWarning("Race update failed: Race {RaceId} not found for Event {EventId}", id, eventId);
                    return false;
                }

                if (HasRaceIdentityChanged(raceEntity, request) &&
                        await IsDuplicateRaceAsync(request))
                {
                    this.ErrorMessage = "Race already exists with the same name and date.";
                    _logger.LogWarning("Duplicate race update attempt: {Name} by User {UserId}", request.Title, currentUserId);
                    return false;
                }

                UpdateRaceEntity(raceEntity, request, currentUserId);
                await SaveRaceChangesAsync(raceEntity);

                // ============================================
                // Handle leaderboard settings override
                // ============================================
                if (request.LeaderboardSettings != null && request.OverrideSettings.HasValue && request.OverrideSettings.Value)
                {
                    var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();

                    // Check if race already has override settings
                    var existingRaceSettings = await leaderboardRepo
                        .GetQuery(lb =>
                            lb.EventId == raceEntity.EventId &&
                            lb.RaceId == raceEntity.Id &&
                            lb.AuditProperties.IsActive &&
                            !lb.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (existingRaceSettings == null)
                    {
                        // ✅ CREATE new race-level override
                        var raceLevelLb = CreateRaceLevelLeaderboardSettings(
                            request.LeaderboardSettings,
                            currentUserId,
                            raceEntity.EventId,
                            raceEntity.Id);

                        await leaderboardRepo.AddAsync(raceLevelLb);
                    }
                    else
                    {
                        // ✅ UPDATE existing race-level override
                        // Entity is already tracked, just modify it
                        _mapper.Map(request.LeaderboardSettings, existingRaceSettings);
                        UpdateAuditProperties(existingRaceSettings.AuditProperties, currentUserId);

                        // ❌ REMOVE THIS LINE - Entity is already tracked!
                        // await leaderboardRepo.UpdateAsync(existingRaceSettings);

                        // EF will automatically detect changes when you call SaveChanges
                    }

                    await _repository.SaveChangesAsync();
                }
                else if (!(request.OverrideSettings ?? false))
                {
                    // ✅ If override is turned OFF, soft delete race-level settings
                    var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();
                    var existingRaceSettings = await leaderboardRepo
                        .GetQuery(lb =>
                            lb.EventId == raceEntity.EventId &&
                            lb.RaceId == raceEntity.Id &&
                            lb.AuditProperties.IsActive &&
                            !lb.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (existingRaceSettings != null)
                    {
                        // Entity is already tracked, just modify it
                        existingRaceSettings.AuditProperties.IsDeleted = true;
                        existingRaceSettings.AuditProperties.IsActive = false;
                        UpdateAuditProperties(existingRaceSettings.AuditProperties, currentUserId);

                        // ❌ REMOVE THIS LINE - Entity is already tracked!
                        // await leaderboardRepo.UpdateAsync(existingRaceSettings);

                        await _repository.SaveChangesAsync();
                    }
                }

                _logger.LogInformation("Race updated successfully: {RaceId}", id);
                return true;
            }
            catch (DbUpdateException dbEx)
            {
                this.ErrorMessage = "Database error occurred while updating the race.";
                _logger.LogError(dbEx, "Database error during race update for ID: {EventId}", id);
                return false;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An unexpected error occurred while updating the race.";
                _logger.LogError(ex, "Error during race update for ID: {RaceId}", id);
                return false;
            }
        }

        public async Task<bool> Delete(string eId, string id)
        {
            try
            {
                var raceRepo = _repository.GetRepository<Race>();

                var eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));
                var raceId = Convert.ToInt32(_encryptionService.Decrypt(id));

                // Load only the Race entity (do NOT include Event or other navigation collections)
                var raceEntity = await raceRepo.GetQuery(e =>
                    e.EventId == eventId &&
                    e.Id == raceId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (raceEntity == null)
                {
                    this.ErrorMessage = $"Race with ID {id} not found or you don't have permission to delete it.";
                    _logger.LogWarning("Race deletion failed: Race {RaceId} not found.", id);
                    return false;
                }

                // Soft delete: Mark as deleted. Event navigation is not loaded so no nested graphs are affected.
                raceEntity.AuditProperties.IsActive = false;
                raceEntity.AuditProperties.IsDeleted = true;
                raceEntity.AuditProperties.UpdatedDate = DateTime.UtcNow;
                raceEntity.AuditProperties.UpdatedBy = _userContext.UserId;

                await raceRepo.UpdateAsync(raceEntity);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Race deleted successfully: {RaceId} by User {UserId}",
                    id, _userContext.UserId);

                return true;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An error occurred while deleting the race.";
                _logger.LogError(ex, "Error during race deletion for ID: {RaceId}", id);
                return false;
            }
        }


        #region Helpers

        /// <summary>
        /// Fetches event entity with related settings for update operation
        /// </summary>
        private async Task<Race?> GetRaceForUpdateAsync(string id, string eId)
        {
            var raceRepo = _repository.GetRepository<Race>();

            int eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));
            int raceId = Convert.ToInt32(_encryptionService.Decrypt(id));

            return await raceRepo.GetQuery(e =>
                      e.Id == raceId &&
                      e.EventId == eventId &&
                      e.AuditProperties.IsActive &&
                      !e.AuditProperties.IsDeleted)
                      .Include(e => e.RaceSettings)
                      .Include(e => e.LeaderboardSettings)
                      .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Updates race entity and its related settings
        /// </summary>
        private void UpdateRaceEntity(Race raceEntity, RaceRequest request, int currentUserId)
        {
            // Update main race properties
            _mapper.Map(request, raceEntity);

            // Update audit properties
            UpdateAuditProperties(raceEntity.AuditProperties, currentUserId);

            // Update or create race settings
            UpdateRaceSettings(raceEntity, request.RaceSettings, currentUserId);
        }

        /// <summary>
        /// Saves race changes to the database
        /// </summary>
        private async Task SaveRaceChangesAsync(Race raceEntity)
        {
            var raceRepo = _repository.GetRepository<Race>();
            await raceRepo.UpdateAsync(raceEntity);
            await _repository.SaveChangesAsync();
        }

        /// <summary>
        /// Creates race settings entity
        /// </summary>
        private RaceSettings CreateRaceSettings(RaceSettingsRequest request, int userId)
        {
            var raceSettings = _mapper.Map<RaceSettings>(request);
            raceSettings.Id = 0; // Ensure EF Core generates a new ID
            raceSettings.RaceId = 0; // Will be set after Race is saved
            raceSettings.AuditProperties = CreateAuditProperties(userId);
            return raceSettings;
        }

        /// <summary>
        /// Creates race-level leaderboard settings entity (RaceId will be set to created race.Id)
        /// </summary>
        private LeaderboardSettings CreateRaceLevelLeaderboardSettings(LeaderboardSettingsRequest request, int userId, int eventId, int raceId)
        {
            var lb = _mapper.Map<LeaderboardSettings>(request);
            lb.Id = 0;
            lb.EventId = eventId;
            lb.RaceId = raceId;
            lb.OverrideSettings = true;
            lb.AuditProperties = CreateAuditProperties(userId);
            return lb;
        }

        /// <summary>
        /// Updates or creates race settings
        /// </summary>
        private void UpdateRaceSettings(Race raceEntity, RaceSettingsRequest? settingsRequest, int currentUserId)
        {
            if (settingsRequest == null)
            {
                return;
            }

            if (raceEntity.RaceSettings != null)
            {
                _mapper.Map(settingsRequest, raceEntity.RaceSettings);
                UpdateAuditProperties(raceEntity.RaceSettings.AuditProperties, currentUserId);
            }
            else
            {
                raceEntity.RaceSettings = CreateRaceSettings(settingsRequest, currentUserId);
            }
        }

        /// <summary>
        /// Checks if race name or date has changed
        /// </summary>
        private static bool HasRaceIdentityChanged(Race raceEntity, RaceRequest request)
        {
            return raceEntity.Title != request.Title ||
                raceEntity.StartTime != request.StartTime ||
                raceEntity.EndTime != request.EndTime ||
                raceEntity.Distance != request.Distance;
        }

        /// <summary>
        /// Checks if a race with the same name and date already exists
        /// </summary>
        private async Task<bool> IsDuplicateRaceAsync(RaceRequest request, int? excludeRaceId = null)
        {
            var raceRepo = _repository.GetRepository<Race>();

            Expression<Func<Race, bool>> duplicateExpression = e =>
                e.Title == request.Title &&
                e.StartTime == request.StartTime &&
                e.EndTime == request.EndTime &&
                e.Distance == request.Distance &&
                e.AuditProperties.IsActive &&
                !e.AuditProperties.IsDeleted;

            return await raceRepo.GetQuery(duplicateExpression)
                .AsNoTracking()
                .AnyAsync();
        }

        private bool ValidateRaceRequest(RaceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                ErrorMessage = "Race title is required.";
                _logger.LogWarning("Race request missing Title");
                return false;
            }

            if (request.StartTime < DateTime.UtcNow.Date)
            {
                ErrorMessage = "Race start time cannot be in the past.";
                _logger.LogWarning("Past race start time provided: {Date}", request.StartTime);
                return false;
            }

            if (request.EndTime < DateTime.UtcNow.Date && request.EndTime <= request.StartTime)
            {
                ErrorMessage = "Race end time cannot be before or equal to start time.";
                _logger.LogWarning("Invalid race time range provided: {StartTime} - {EndTime}", request.StartTime, request.EndTime);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates the date range in the search request
        /// </summary>
        private bool ValidateDateRange(RaceSearchRequest request)
        {
            if (request.EndTime.HasValue && request.StartTime.HasValue &&
                 request.StartTime.Value > request.EndTime.Value)
            {
                this.ErrorMessage = "StartTime must be less than or equal to EndTime.";
                _logger.LogWarning("Invalid date range in race search: From={From}, To={To}",
               request.StartTime.Value, request.EndTime.Value);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Executes the search query and returns paginated results
        /// </summary>
        private async Task<Models.Data.Common.PagingList<Race>> ExecuteSearchQueryAsync(int eventId, RaceSearchRequest request)
        {
            var raceRepo = _repository.GetRepository<Race>();
            var expression = BuildSearchExpression(eventId, request);
            var mappedSortField = GetMappedSortField(request.SortFieldName);

            return await raceRepo.SearchAsync(
                       expression,
                       request.PageSize,
                       request.PageNumber,
                       request.SortDirection == Models.Client.Common.SortDirection.Ascending
                            ? Models.Data.Common.SortDirection.Ascending
                           : Models.Data.Common.SortDirection.Descending,
                       mappedSortField,
                       false,
                       false);
        }

        /// <summary>
        /// Builds the filter expression for race search
        /// </summary>
        private static Expression<Func<Race, bool>> BuildSearchExpression(int eventId, RaceSearchRequest request)
        {
            return e =>
                e.EventId == eventId &&
                (string.IsNullOrEmpty(request.Title) || e.Title.Contains(request.Title)) &&
                (string.IsNullOrEmpty(request.Description) || e.Description != null && e.Description.Contains(request.Description)) &&
                (!request.Distance.HasValue || e.Distance == request.Distance.Value) &&
                (!request.StartTime.HasValue || e.StartTime.HasValue && e.StartTime.Value.Date == request.StartTime.Value.Date) &&
                (!request.EndTime.HasValue || e.EndTime.HasValue && e.EndTime.Value.Date == request.EndTime.Value.Date) &&
                (!request.MaxParticipants.HasValue || e.MaxParticipants == request.MaxParticipants.Value) &&
                //TODO : Add status conditions
                e.AuditProperties.IsActive &&
                !e.AuditProperties.IsDeleted;
        }

        /// <summary>
        /// Maps sort field name from client format to database format
        /// </summary>
        private string? GetMappedSortField(string? sortFieldName)
        {
            if (string.IsNullOrEmpty(sortFieldName))
            {
                return null;
            }

            if (SortFieldMapping.TryGetValue(sortFieldName, out var dbFieldName))
            {
                return dbFieldName;
            }

            _logger.LogWarning("Unknown sort field '{SortField}' requested, using default sorting", sortFieldName);
            return "Id";
        }

        /// <summary>
        /// Loads races and projects them directly to RaceResponse DTOs using AutoMapper.ProjectTo.
        /// Event collection navigation properties are ignored in the mapping profile so returned Event has no nested Races.
        /// Ensures each race has effective LeaderboardSettings populated: race-level if present, otherwise event-level.
        /// </summary>
        private async Task<List<RaceResponse>> LoadRaceResponsesAsync(Models.Data.Common.PagingList<Race> searchResults)
        {
            if (searchResults == null || searchResults.Count == 0)
            {
                return [];
            }

            var raceRepo = _repository.GetRepository<Race>();
            var orderedIds = searchResults.Select(e => e.Id).ToList();

            var baseQuery = raceRepo.GetQuery(e => orderedIds.Contains(e.Id))
                .Include(e => e.RaceSettings)
                .Include(e => e.Event)
                .AsNoTracking();

            var entities = await baseQuery.ToListAsync();

            var orderedEntities = orderedIds
                .Select(id => entities.FirstOrDefault(e => e.Id == id))
                .Where(e => e != null)
                .ToList()!;

            // Get effective leaderboard settings for each race
            var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();

            foreach (var race in orderedEntities)
            {
                // Get effective leaderboard settings using the helper method
                var effectiveSettings = await GetEffectiveLeaderboardSettings(race.EventId, race.Id);

                // Attach to race for mapping
                race.LeaderboardSettings = effectiveSettings;
            }

            var responses = _mapper.Map<List<RaceResponse>>(orderedEntities);
            return responses;
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

        /// <summary>
        /// Updates audit properties for an entity
        /// </summary>
        private static void UpdateAuditProperties(Models.Data.Common.AuditProperties auditProperties, int userId)
        {
            auditProperties.UpdatedDate = DateTime.UtcNow;
            auditProperties.UpdatedBy = userId;
        }

        // Map client-facing property names to database property names
        private static readonly Dictionary<string, string> SortFieldMapping = new(StringComparer.OrdinalIgnoreCase)
        {
             { "CreatedAt", "AuditProperties.CreatedDate" },
             { "UpdatedAt", "AuditProperties.UpdatedDate" }
        };

        #endregion
    }
}