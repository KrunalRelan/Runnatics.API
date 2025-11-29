using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;
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


        public async Task<PagingList<RaceResponse>> Search(string eId, RaceSearchRequest request)
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

                // Project to DTOs (AutoMapper.ProjectTo) — Event collections are ignored in the mapping profile
                var raceResponses = await LoadRaceResponsesAsync(searchResults);

                // Create paging result
                var paging = new PagingList<RaceResponse>(raceResponses)
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

        public async Task<bool> Create(string eId, RaceRequest request)
        {
            // Validate request
            if (!ValidateRaceRequest(request))
            {
                return false;
            }

            var eventId = Convert.ToInt32(_encryptionService.Decrypt(eId));

            var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

            try
            {
                // Quick existence and tenant-scope check for parent Event
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
                    _logger.LogWarning("Race creation aborted - event not found or inactive. EventId: {EventId}, UserId: {UserId}", eventId, currentUserId);
                    return false;
                }

                await _repository.BeginTransactionAsync();

                try
                {
                    // Map DTO to domain entity and set service controlled fields
                    var race = _mapper.Map<Race>(request);
                    race.EventId = eventId;

                    race.AuditProperties = CreateAuditProperties(currentUserId);

                    // If settings supplied, map and attach to the race so EF can persist in one SaveChanges
                    if (request.RaceSettings != null)
                    {
                        race.RaceSettings = CreateRaceSettings(request.RaceSettings, currentUserId);
                    }

                    var raceRepo = _repository.GetRepository<Race>();
                    await raceRepo.AddAsync(race);

                    // Save race first to obtain race.Id for race-level leaderboard settings
                    await _repository.SaveChangesAsync();

                    // If leaderboard settings provided at race-level, create a LeaderboardSettings record
                    // with RaceId set to the newly created race Id and OverrideSettings set to true.
                    if (request.LeaderboardSettings != null)
                    {
                        var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();
                        var raceLevelLb = CreateRaceLevelLeaderboardSettings(request.LeaderboardSettings, currentUserId, eventId, race.Id);
                        await leaderboardRepo.AddAsync(raceLevelLb);
                        await _repository.SaveChangesAsync();
                    }

                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation("Race created successfully. RaceId: {RaceId}, EventId: {EventId}, CreatedBy: {UserId}", race.Id, race.EventId, currentUserId);
                    return true;
                }
                catch (Exception exInner)
                {
                    try
                    {
                        await _repository.RollbackTransactionAsync();
                    }
                    catch (Exception rbEx)
                    {
                        _logger.LogWarning(rbEx, "Rollback failed after error while creating race for EventId: {EventId}", eventId);
                    }

                    _logger.LogError(exInner, "Error creating race for EventId: {EventId}", eventId);
                    ErrorMessage = "Error creating race.";
                    return false;
                }
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error while creating race for EventId: {EventId}", eventId);
                ErrorMessage = "Database error occurred while creating the race.";
                return false;
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogWarning(uaEx, "Unauthorized race creation attempt");
                ErrorMessage = "Unauthorized: " + uaEx.Message;
                return false;
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

                // Load the entity from the database first (EF returns numeric Ids)
                // and then map to the DTO in-memory. This avoids AutoMapper.ProjectTo
                // trying to build an expression that converts numeric IDs to strings
                // (or invoking services) which cannot be translated to SQL.
                var raceDb = await raceRepo.GetQuery(e =>
                                                       e.Id == raceId &&
                                                       e.EventId == eventId &&
                                                       e.AuditProperties.IsActive &&
                                                       !e.AuditProperties.IsDeleted)
                                                       .Include(e => e.RaceSettings)
                                                       .Include(e => e.Event)
                                                       .Include(e => e.LeaderboardSettings)
                                                       .AsNoTracking()
                                                       .FirstOrDefaultAsync();

                if (raceDb == null)
                {
                    this.ErrorMessage = "Race not found.";
                    _logger.LogWarning("Race with ID {RaceId} not found.", id);
                    return null;
                }

                // Map in-memory so any converters/resolvers (e.g. encryption to produce string IDs)
                // run in CLR rather than being part of the EF expression tree.
                var raceEntity = _mapper.Map<RaceResponse>(raceDb);

                return raceEntity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving race : {Id}", id);
                this.ErrorMessage = "Error retrieving race.";
                return null;
            }
        }

        public async Task<bool> Update(string eventId, string id, RaceRequest request)
        {
            try
            {
                var currentUserId = _userContext.UserId;

                // Validate request
                if (!ValidateRaceRequest(request))
                {
                    return false;
                }

                // Fetch event with related entities in a single query
                var raceEntity = await GetRaceForUpdateAsync(id, eventId);
                if (raceEntity == null)
                {
                    this.ErrorMessage = $"Race with ID {id} not found or you don't have permission to update it.";
                    _logger.LogWarning("Race update failed: Race {RaceId} not found for Event {EventId}", id, eventId);
                    return false;
                }

                // Check for duplicates only if name or date changed
                if (HasRaceIdentityChanged(raceEntity, request) &&
                        await IsDuplicateRaceAsync(request))
                {
                    this.ErrorMessage = "Race already exists with the same name and date.";
                    _logger.LogWarning("Duplicate race update attempt: {Name} by User {UserId}", request.Title, currentUserId);
                    return false;
                }

                // Update event and related entities
                UpdateRaceEntity(raceEntity, request, currentUserId);

                // Persist changes
                await SaveRaceChangesAsync(raceEntity);

                // If request contains race-level leaderboard settings, ensure RaceId and OverrideSettings are set
                if (request.LeaderboardSettings != null)
                {
                    var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();
                    if (raceEntity.LeaderboardSettings == null)
                    {
                        var raceLevelLb = CreateRaceLevelLeaderboardSettings(request.LeaderboardSettings, currentUserId, raceEntity.EventId, raceEntity.Id);
                        await leaderboardRepo.AddAsync(raceLevelLb);
                    }
                    else
                    {
                        // Update existing race-level leaderboard settings and ensure OverrideSettings true
                        _mapper.Map(request.LeaderboardSettings, raceEntity.LeaderboardSettings);
                        raceEntity.LeaderboardSettings.OverrideSettings = true;
                        raceEntity.LeaderboardSettings.RaceId = raceEntity.Id;
                        UpdateAuditProperties(raceEntity.LeaderboardSettings.AuditProperties, currentUserId);
                        await leaderboardRepo.UpdateAsync(raceEntity.LeaderboardSettings);
                    }
                    await _repository.SaveChangesAsync();
                }

                _logger.LogInformation("Race updated successfully: {RaceId} - {Name} by User {UserId}",
                                            id, raceEntity.Title, currentUserId);

                return true;
            }
            catch (DbUpdateException dbEx)
            {
                this.ErrorMessage = "Database error occurred while updating the event.";
                _logger.LogError(dbEx, "Database error during event update for ID: {EventId}", id);
                return false;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An unexpected error occurred while updating the event.";
                _logger.LogError(ex, "Error during event update for ID: {EventId}", id);
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
                       request.SortDirection == SortDirection.Ascending
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
        /// </summary>
        private async Task<List<RaceResponse>> LoadRaceResponsesAsync(Models.Data.Common.PagingList<Race> searchResults)
        {
            if (searchResults == null || searchResults.Count == 0)
            {
                return [];
            }

            var raceRepo = _repository.GetRepository<Race>();
            var orderedIds = searchResults.Select(e => e.Id).ToList();

            // Prepare base query for the relevant races
            var baseQuery = raceRepo.GetQuery(e => orderedIds.Contains(e.Id)).AsNoTracking();

            // Fetch entities from DB (EF will return numeric Ids)
            var entities = await baseQuery.ToListAsync();

            // Restore original order based on orderedIds (safely handle any missing items)
            var orderedEntities = orderedIds
                .Select(id => entities.FirstOrDefault(e => e.Id == id))
                .Where(e => e != null)
                .ToList()!;

            // Map entities to DTOs in-memory. Mapper will still produce the encrypted string Ids as configured.
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