using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Linq.Expressions;

namespace Runnatics.Services
{
    public class EventsService(IUnitOfWork<RaceSyncDbContext> repository,
                               IMapper mapper,
                               ILogger<EventsService> logger,
                               IConfiguration configuration,
                               IUserContextService userContext) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventsService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;

        // Map client-facing property names to database property names
        private static readonly Dictionary<string, string> SortFieldMapping = new(StringComparer.OrdinalIgnoreCase)
        {
             { "CreatedAt", "AuditProperties.CreatedDate" },
             { "UpdatedAt", "AuditProperties.UpdatedDate" }
        };

        public async Task<PagingList<EventResponse>> Search(EventSearchRequest request)
        {
            try
            {
                var organizationId = _userContext.OrganizationId;

                // Validate date range
                if (!ValidateDateRange(request))
                {
                    return [];
                }

                // Build and execute search query
                var searchResults = await ExecuteSearchQueryAsync(request, organizationId);

                // Load navigation properties while preserving sort order
                var eventsWithDetails = await LoadEventsWithNavigationPropertiesAsync(searchResults);

                // Map to response and return
                var response = MapToEventResponseList(eventsWithDetails, searchResults.TotalCount);

                _logger.LogInformation("Event search completed for Organization {OrgId} by User {UserId}. Found {Count} events.",
               organizationId, _userContext.UserId, response.TotalCount);

                return response;
            }
            catch (UnauthorizedAccessException ex)
            {
                this.ErrorMessage = "Unauthorized: " + ex.Message;
                _logger.LogWarning(ex, "Unauthorized event search attempt");
                return [];
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error during event search");
                return [];
            }
        }

        #region Search Helper Methods

        /// <summary>
        /// Validates the date range in the search request
        /// </summary>
        private bool ValidateDateRange(EventSearchRequest request)
        {
            if (request.EventDateFrom.HasValue && request.EventDateTo.HasValue &&
         request.EventDateFrom.Value > request.EventDateTo.Value)
            {
                this.ErrorMessage = "EventDateFrom must be less than or equal to EventDateTo.";
                _logger.LogWarning("Invalid date range in event search: From={From}, To={To}",
               request.EventDateFrom.Value, request.EventDateTo.Value);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builds the filter expression for event search
        /// </summary>
        private static Expression<Func<Event, bool>> BuildSearchExpression(EventSearchRequest request, int organizationId)
        {
            return e =>
                e.OrganizationId == organizationId &&
                (!request.Id.HasValue || e.Id == request.Id.Value) &&
                (string.IsNullOrEmpty(request.Name) || e.Name.Contains(request.Name)) &&
                (string.IsNullOrEmpty(request.Status) || e.Status == request.Status) &&
                (!request.EventDateFrom.HasValue || e.EventDate >= request.EventDateFrom.Value) &&
                (!request.EventDateTo.HasValue || e.EventDate <= request.EventDateTo.Value) &&
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
        /// Executes the search query and returns paginated results
        /// </summary>
        private async Task<Models.Data.Common.PagingList<Event>> ExecuteSearchQueryAsync(EventSearchRequest request, int organizationId)
        {
            var eventRepo = _repository.GetRepository<Event>();
            var expression = BuildSearchExpression(request, organizationId);
            var mappedSortField = GetMappedSortField(request.SortFieldName);

            return await eventRepo.SearchAsync(
                       expression,
                       request.PageSize,
                       request.PageNumber,
                       request.SortDirection == SortDirection.Ascending
                            ? Models.Data.Common.SortDirection.Ascending
                           : Models.Data.Common.SortDirection.Descending,
                       mappedSortField,
                       ignoreQueryFilters: false,
                       includeNavigationProperties: false);
        }

        /// <summary>
        /// Loads events with navigation properties while preserving original sort order
        /// </summary>
        private async Task<List<Event>> LoadEventsWithNavigationPropertiesAsync(Models.Data.Common.PagingList<Event> searchResults)
        {
            if (searchResults.Count == 0)
            {
                return [];
            }

            var eventRepo = _repository.GetRepository<Event>();
            var orderedIds = searchResults.Select(e => e.Id).ToList();

            var eventsWithDetails = await eventRepo.GetQuery(e => orderedIds.Contains(e.Id))
                    .Include(e => e.EventSettings)
                    .Include(e => e.LeaderboardSettings)
                    .Include(e => e.Organization)
                    .AsNoTracking()
                    .ToListAsync();

            // Restore original sort order
            return [.. orderedIds.Select(id => eventsWithDetails.First(e => e.Id == id))];
        }

        /// <summary>
        /// Maps event entities to response DTOs
        /// </summary>
        private PagingList<EventResponse> MapToEventResponseList(List<Event> events, int totalCount)
        {
            var mappedData = _mapper.Map<PagingList<EventResponse>>(events);
            mappedData.TotalCount = totalCount;
            return mappedData;
        }

        #endregion

        public async Task<bool> Create(EventRequest request)
        {
            try
            {
                // Get user ID and organization ID from JWT token
                var currentUserId = _userContext.UserId;
                var organizationId = _userContext.OrganizationId;

                // Override request organization ID with token organization ID for security
                request.OrganizationId = organizationId;

                // Validate request
                if (!ValidateEventRequest(request))
                {
                    return false;
                }

                // Check for duplicates
                if (await IsDuplicateEventAsync(request))
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    _logger.LogWarning("Duplicate event creation attempt: {Name} on {Date} for Organization {OrgId} by User {UserId}",
                                                request.Name, request.EventDate, organizationId, currentUserId);
                    return false;
                }

                // Create event entity with settings
                var eventEntity = CreateEventEntity(request);

                // Persist to database
                var createdEventId = await SaveEventAsync(eventEntity);

                _logger.LogInformation("Event created successfully: {EventId} - {Name} by User {UserId}",
                      createdEventId, eventEntity.Name, currentUserId);

                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                this.ErrorMessage = "Unauthorized: " + ex.Message;
                _logger.LogWarning(ex, "Unauthorized event creation attempt");
                return false;
            }
            catch (DbUpdateException dbEx)
            {
                this.ErrorMessage = "Database error occurred while creating the event.";
                _logger.LogError(dbEx, "Database error during event creation for: {Name}", request.Name);
                return false;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An unexpected error occurred while creating the event.";
                _logger.LogError(ex, "Error during event creation for: {Name}", request.Name);
                return false;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates the event request
        /// </summary>
        private bool ValidateEventRequest(EventRequest request)
        {
            if (request == null)
            {
                this.ErrorMessage = "Event request cannot be null.";
                _logger.LogWarning("Null event request received");
                return false;
            }

            if (request.EventDate < DateTime.UtcNow.Date)
            {
                this.ErrorMessage = "Event date cannot be in the past.";
                _logger.LogWarning("Past event date provided: {Date}", request.EventDate);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if an event with the same name and date already exists
        /// </summary>
        private async Task<bool> IsDuplicateEventAsync(EventRequest request, int? excludeEventId = null)
        {
            var eventRepo = _repository.GetRepository<Event>();

            Expression<Func<Event, bool>> duplicateExpression = e =>
                 e.Name == request.Name &&
        e.EventDate.Date == request.EventDate.Date &&
                  e.OrganizationId == request.OrganizationId &&
              e.AuditProperties.IsActive &&
      !e.AuditProperties.IsDeleted &&
                  (!excludeEventId.HasValue || e.Id != excludeEventId.Value);

            return await eventRepo.GetQuery(duplicateExpression)
              .AsNoTracking()
           .AnyAsync();
        }

        /// <summary>
        /// Creates the event entity with all related settings
        /// </summary>
        private Event CreateEventEntity(EventRequest request)
        {
            // Map base event entity
            var eventEntity = _mapper.Map<Event>(request);

            // Get current user ID from context (already set in Create method)
            var currentUserId = _userContext.UserId;
            eventEntity.AuditProperties = CreateAuditProperties(currentUserId);

            // Add event settings if provided
            if (request.EventSettings != null)
            {
                eventEntity.EventSettings = CreateEventSettings(request.EventSettings, currentUserId);
            }

            // Add leaderboard settings if provided
            if (request.LeaderboardSettings != null)
            {
                eventEntity.LeaderboardSettings = CreateLeaderboardSettings(request.LeaderboardSettings, currentUserId);
            }

            return eventEntity;
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
        /// Creates event settings entity
        /// </summary>
        private EventSettings CreateEventSettings(EventSettingsRequest request, int userId)
        {
            var eventSettings = _mapper.Map<EventSettings>(request);
            eventSettings.AuditProperties = CreateAuditProperties(userId);
            return eventSettings;
        }

        /// <summary>
        /// Creates leaderboard settings entity
        /// </summary>
        private LeaderboardSettings CreateLeaderboardSettings(LeaderboardSettingsRequest request, int userId)
        {
            var leaderboardSettings = _mapper.Map<LeaderboardSettings>(request);
            leaderboardSettings.AuditProperties = CreateAuditProperties(userId);
            return leaderboardSettings;
        }

        /// <summary>
        /// Saves the event entity to the database
        /// </summary>
        private async Task<int> SaveEventAsync(Event eventEntity)
        {
            var eventRepo = _repository.GetRepository<Event>();
            await eventRepo.AddAsync(eventEntity);
            await _repository.SaveChangesAsync();
            return eventEntity.Id;
        }

        /// <summary>
        /// Retrieves the complete event response with all related data
        /// </summary>
        private async Task<EventResponse?> GetEventResponseAsync(int eventId)
        {
            var eventRepo = _repository.GetRepository<Event>();

            var createdEvent = await eventRepo.GetQuery(e => e.Id == eventId)
      .Include(e => e.EventSettings)
                .Include(e => e.LeaderboardSettings)
             .Include(e => e.Organization)
          .AsNoTracking()
          .FirstOrDefaultAsync();

            if (createdEvent == null)
            {
                this.ErrorMessage = "Event was created but could not be retrieved.";
                _logger.LogError("Failed to retrieve event after creation: {EventId}", eventId);
                return null;
            }

            return _mapper.Map<EventResponse>(createdEvent);
        }

        #endregion

        public async Task<bool> Delete(int id)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                var organizationId = _userContext.OrganizationId;

                // Find the event and verify it belongs to the user's organization
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Id == id &&
                    e.OrganizationId == organizationId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (eventEntity == null)
                {
                    this.ErrorMessage = $"Event with ID {id} not found or you don't have permission to delete it.";
                    _logger.LogWarning("Event deletion failed: Event {EventId} not found for Organization {OrgId}",
                       id, organizationId);
                    return false;
                }

                // Soft delete: Mark as deleted
                eventEntity.AuditProperties.IsDeleted = true;
                eventEntity.AuditProperties.UpdatedDate = DateTime.UtcNow;
                eventEntity.AuditProperties.UpdatedBy = _userContext.UserId;

                await eventRepo.UpdateAsync(eventEntity);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event deleted successfully: {EventId} - {Name} by User {UserId}",
                              id, eventEntity.Name, _userContext.UserId);

                return true;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An error occurred while deleting the event.";
                _logger.LogError(ex, "Error during event deletion for ID: {EventId}", id);
                return false;
            }
        }

        public async Task<bool> Update(int id, EventRequest request)
        {
            try
            {
                var currentUserId = _userContext.UserId;
                var organizationId = _userContext.OrganizationId;

                // Override request organization ID with token organization ID for security
                request.OrganizationId = organizationId;

                // Validate request
                if (!ValidateEventRequest(request))
                {
                    return false;
                }

                // Fetch event with related entities in a single query
                var eventEntity = await GetEventForUpdateAsync(id, organizationId);
                if (eventEntity == null)
                {
                    this.ErrorMessage = $"Event with ID {id} not found or you don't have permission to update it.";
                    _logger.LogWarning("Event update failed: Event {EventId} not found for Organization {OrgId}",
           id, organizationId);
                    return false;
                }

                // Check for duplicates only if name or date changed
                if (HasEventIdentityChanged(eventEntity, request) &&
                        await IsDuplicateEventAsync(request, id))
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    _logger.LogWarning("Duplicate event update attempt: {Name} on {Date} for Organization {OrgId} by User {UserId}",
                            request.Name, request.EventDate, organizationId, currentUserId);
                    return false;
                }

                // Update event and related entities
                UpdateEventEntity(eventEntity, request, currentUserId);

                // Persist changes
                await SaveEventChangesAsync(eventEntity);

                _logger.LogInformation("Event updated successfully: {EventId} - {Name} by User {UserId}",
                                            id, eventEntity.Name, currentUserId);

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

        #region Update Helper Methods

        /// <summary>
        /// Fetches event entity with related settings for update operation
        /// </summary>
        private async Task<Event?> GetEventForUpdateAsync(int id, int organizationId)
        {
            var eventRepo = _repository.GetRepository<Event>();

            return await eventRepo.GetQuery(e =>
                      e.Id == id &&
                      e.OrganizationId == organizationId &&
                      e.AuditProperties.IsActive &&
                      !e.AuditProperties.IsDeleted)
                      .Include(e => e.EventSettings)
                      .Include(e => e.LeaderboardSettings)
                      .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Checks if event name or date has changed
        /// </summary>
        private static bool HasEventIdentityChanged(Event eventEntity, EventRequest request)
        {
            return eventEntity.Name != request.Name ||
                eventEntity.EventDate.Date != request.EventDate.Date;
        }

        /// <summary>
        /// Updates event entity and its related settings
        /// </summary>
        private void UpdateEventEntity(Event eventEntity, EventRequest request, int currentUserId)
        {
            // Update main event properties
            _mapper.Map(request, eventEntity);

            // Update audit properties
            UpdateAuditProperties(eventEntity.AuditProperties, currentUserId);

            // Update or create event settings
            UpdateEventSettings(eventEntity, request.EventSettings, currentUserId);

            // Update or create leaderboard settings
            UpdateLeaderboardSettings(eventEntity, request.LeaderboardSettings, currentUserId);
        }

        /// <summary>
        /// Updates audit properties for an entity
        /// </summary>
        private static void UpdateAuditProperties(Models.Data.Common.AuditProperties auditProperties, int userId)
        {
            auditProperties.UpdatedDate = DateTime.UtcNow;
            auditProperties.UpdatedBy = userId;
        }

        /// <summary>
        /// Updates or creates event settings
        /// </summary>
        private void UpdateEventSettings(Event eventEntity, EventSettingsRequest? settingsRequest, int currentUserId)
        {
            if (settingsRequest == null)
            {
                return;
            }

            if (eventEntity.EventSettings != null)
            {
                _mapper.Map(settingsRequest, eventEntity.EventSettings);
                UpdateAuditProperties(eventEntity.EventSettings.AuditProperties, currentUserId);
            }
            else
            {
                eventEntity.EventSettings = CreateEventSettings(settingsRequest, currentUserId);
            }
        }

        /// <summary>
        /// Updates or creates leaderboard settings
        /// </summary>
        private void UpdateLeaderboardSettings(Event eventEntity, LeaderboardSettingsRequest? settingsRequest, int currentUserId)
        {
            if (settingsRequest == null)
            {
                return;
            }

            if (eventEntity.LeaderboardSettings != null)
            {
                _mapper.Map(settingsRequest, eventEntity.LeaderboardSettings);
                UpdateAuditProperties(eventEntity.LeaderboardSettings.AuditProperties, currentUserId);
            }
            else
            {
                eventEntity.LeaderboardSettings = CreateLeaderboardSettings(settingsRequest, currentUserId);
            }
        }

        /// <summary>
        /// Saves event changes to the database
        /// </summary>
        private async Task SaveEventChangesAsync(Event eventEntity)
        {
            var eventRepo = _repository.GetRepository<Event>();
            await eventRepo.UpdateAsync(eventEntity);
            await _repository.SaveChangesAsync();
        }

        #endregion
    }
}
