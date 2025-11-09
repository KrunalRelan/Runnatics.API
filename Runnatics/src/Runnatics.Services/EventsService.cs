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
                               IUserContextService userContext) :
        ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventsService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;

        // Map client-facing property names to database property names
        private static readonly Dictionary<string, string> SortFieldMapping = new(StringComparer.OrdinalIgnoreCase)
        {
{ "CreatedAt", "AuditProperties.CreatedDate" },
  { "UpdatedAt", "AuditProperties.UpdatedDate" },
    { "Id", "Id" },
             { "Name", "Name" },
             { "EventDate", "EventDate" },
        { "Status", "Status" }
   };

        public async Task<PagingList<EventResponse>> Search(EventSearchRequest request)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                var organizationId = _userContext.OrganizationId;

                // Build expression with OrganizationId from token as the primary filter
                // When no other filters are provided, returns all events for the organization
                Expression<Func<Event, bool>> expression = e =>
                    e.OrganizationId == organizationId &&
                (!request.Id.HasValue || e.Id == request.Id.Value) &&
                    (string.IsNullOrEmpty(request.Name) || e.Name.Contains(request.Name)) &&
                               (string.IsNullOrEmpty(request.Status) || e.Status == request.Status) &&
               (!request.EventDateFrom.HasValue || e.EventDate >= request.EventDateFrom.Value) &&
                     (!request.EventDateTo.HasValue || e.EventDate <= request.EventDateTo.Value) &&
                     e.AuditProperties.IsActive &&
             !e.AuditProperties.IsDeleted;

                // Map the sort field name from client format to database format
                string? mappedSortField = null;
                if (!string.IsNullOrEmpty(request.SortFieldName))
                {
                    if (SortFieldMapping.TryGetValue(request.SortFieldName, out var dbFieldName))
                    {
                        mappedSortField = dbFieldName;
                    }
                    else
                    {
                        _logger.LogWarning("Unknown sort field '{SortField}' requested, using default sorting", request.SortFieldName);
                        mappedSortField = "AuditProperties.CreatedDate"; // Default fallback
                    }
                }

                var data = await eventRepo.SearchAsync(expression,
                                                        request.PageSize,
                                                        request.PageNumber,
                                                        request.SortDirection == SortDirection.Ascending ?
                                                        Models.Data.Common.SortDirection.Ascending :
                                                        Models.Data.Common.SortDirection.Descending,
                                                        mappedSortField,
                                                        false,
                                                        false);

                // Preserve the sort order by storing IDs in order
                var orderedIds = data.Select(d => d.Id).ToList();

                // Manually load only the navigation properties we need
                var eventsWithDetails = await eventRepo.GetQuery(e => orderedIds.Contains(e.Id))
                            .Include(e => e.EventSettings)
                            .Include(e => e.LeaderboardSettings)
                            .Include(e => e.Organization)
                            .AsNoTracking()
                            .ToListAsync();

                // Restore the original sort order
                var sortedEvents = orderedIds
                 .Select(id => eventsWithDetails.First(e => e.Id == id))
                    .ToList();

                var mappedData = _mapper.Map<PagingList<EventResponse>>(sortedEvents);
                mappedData.TotalCount = data.TotalCount;

                _logger.LogInformation("Event search completed for Organization {OrgId} by User {UserId}. Found {Count} events.",
           organizationId, _userContext.UserId, mappedData.TotalCount);

                return mappedData;
            }
            catch (UnauthorizedAccessException ex)
            {
                this.ErrorMessage = "Unauthorized: " + ex.Message;
                _logger.LogWarning(ex, "Unauthorized event search attempt");
                return new PagingList<EventResponse>();
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error during event search");
                return [];
            }
        }

        public async Task<EventResponse?> Create(EventRequest request)
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
                    return null;
                }

                // Check for duplicates
                if (await IsDuplicateEventAsync(request))
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    _logger.LogWarning("Duplicate event creation attempt: {Name} on {Date} for Organization {OrgId} by User {UserId}",
              request.Name, request.EventDate, organizationId, currentUserId);
                    return null;
                }

                // Create event entity with settings
                var eventEntity = CreateEventEntity(request);

                // Persist to database
                var createdEventId = await SaveEventAsync(eventEntity);

                _logger.LogInformation("Event created successfully: {EventId} - {Name} by User {UserId}",
                      createdEventId, eventEntity.Name, currentUserId);

                // Return response with full details
                return await GetEventResponseAsync(createdEventId);
            }
            catch (UnauthorizedAccessException ex)
            {
                this.ErrorMessage = "Unauthorized: " + ex.Message;
                _logger.LogWarning(ex, "Unauthorized event creation attempt");
                return null;
            }
            catch (DbUpdateException dbEx)
            {
                this.ErrorMessage = "Database error occurred while creating the event.";
                _logger.LogError(dbEx, "Database error during event creation for: {Name}", request.Name);
                return null;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An unexpected error occurred while creating the event.";
                _logger.LogError(ex, "Error during event creation for: {Name}", request.Name);
                return null;
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

        public async Task<EventResponse?> Update(int id, EventRequest request)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                var organizationId = _userContext.OrganizationId;
                var currentUserId = _userContext.UserId;

                // Find the event and verify it belongs to the user's organization
                var eventEntity = await eventRepo.GetQuery(e =>
                   e.Id == id &&
                      e.OrganizationId == organizationId &&
              e.AuditProperties.IsActive &&
              !e.AuditProperties.IsDeleted)
                            .Include(e => e.EventSettings)
              .Include(e => e.LeaderboardSettings)
                        .FirstOrDefaultAsync();

                if (eventEntity == null)
                {
                    this.ErrorMessage = $"Event with ID {id} not found or you don't have permission to update it.";
                    _logger.LogWarning("Event update failed: Event {EventId} not found for Organization {OrgId}",
                            id, organizationId);
                    return null;
                }

                // Override request organization ID with token organization ID for security
                request.OrganizationId = organizationId;

                // Validate request
                if (!ValidateEventRequest(request))
                {
                    return null;
                }

                // Check for duplicates when name or date is changed
                if (eventEntity.Name != request.Name || eventEntity.EventDate.Date != request.EventDate.Date)
                {
                    if (await IsDuplicateEventAsync(request, id))
                    {
                        this.ErrorMessage = "Event already exists with the same name and date.";
                        _logger.LogWarning("Duplicate event update attempt: {Name} on {Date} for Organization {OrgId} by User {UserId}",
                  request.Name, request.EventDate, organizationId, currentUserId);
                        return null;
                    }
                }

                // Update event properties
                _mapper.Map(request, eventEntity);

                // Update audit properties
                eventEntity.AuditProperties.UpdatedDate = DateTime.UtcNow;
                eventEntity.AuditProperties.UpdatedBy = currentUserId;

                // Update or create event settings if provided
                if (request.EventSettings != null)
                {
                    if (eventEntity.EventSettings != null)
                    {
                        _mapper.Map(request.EventSettings, eventEntity.EventSettings);
                        eventEntity.EventSettings.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        eventEntity.EventSettings.AuditProperties.UpdatedBy = currentUserId;
                    }
                    else
                    {
                        eventEntity.EventSettings = CreateEventSettings(request.EventSettings, currentUserId);
                    }
                }

                // Update or create leaderboard settings if provided
                if (request.LeaderboardSettings != null)
                {
                    if (eventEntity.LeaderboardSettings != null)
                    {
                        _mapper.Map(request.LeaderboardSettings, eventEntity.LeaderboardSettings);
                        eventEntity.LeaderboardSettings.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        eventEntity.LeaderboardSettings.AuditProperties.UpdatedBy = currentUserId;
                    }
                    else
                    {
                        eventEntity.LeaderboardSettings = CreateLeaderboardSettings(request.LeaderboardSettings, currentUserId);
                    }
                }

                // Save changes
                await eventRepo.UpdateAsync(eventEntity);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event updated successfully: {EventId} - {Name} by User {UserId}",
              id, eventEntity.Name, currentUserId);

                // Return updated event with all details
                return await GetEventResponseAsync(id);
            }
            catch (DbUpdateException dbEx)
            {
                this.ErrorMessage = "Database error occurred while updating the event.";
                _logger.LogError(dbEx, "Database error during event update for ID: {EventId}", id);
                return null;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "An unexpected error occurred while updating the event.";
                _logger.LogError(ex, "Error during event update for ID: {EventId}", id);
                return null;
            }
        }
    }
}
