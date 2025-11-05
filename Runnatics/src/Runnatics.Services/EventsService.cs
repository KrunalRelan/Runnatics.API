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
    public class EventsService(IUnitOfWork<RaceSyncDbContext> repository, IMapper mapper, ILogger<EventsService> logger, IConfiguration configuration) :
        ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventsService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;

        public async Task<PagingList<EventResponse>> Search(EventSearchRequest request)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                Expression<Func<Event, bool>> expression = e =>
                         (string.IsNullOrEmpty(request.Name) || e.Name.Contains(request.Name)) &&
                         (string.IsNullOrEmpty(request.Status) || e.Status == request.Status) &&
                         (!request.EventDateFrom.HasValue || e.EventDate >= request.EventDateFrom.Value) &&
                         (!request.EventDateTo.HasValue || e.EventDate <= request.EventDateTo.Value) &&
                         e.AuditProperties.IsActive &&
                         !e.AuditProperties.IsDeleted;

                var data = await eventRepo.SearchAsync(expression, request.PageSize,
                    request.PageNumber,
                    request.SortDirection == SortDirection.Ascending ?
                    Models.Data.Common.SortDirection.Ascending :
                    Models.Data.Common.SortDirection.Descending,
                    false,
                    request.SortFieldName,
                    true // Include navigation properties to load EventSettings and Organization
                );

                var mappedData = _mapper.Map<PagingList<EventResponse>>(data);
                return mappedData;
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
                // Validate request
                if (!ValidateEventRequest(request))
                {
                    return null;
                }

                // Check for duplicates
                if (await IsDuplicateEventAsync(request))
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    _logger.LogWarning("Duplicate event creation attempt: {Name} on {Date} for Organization {OrgId}",
                               request.Name, request.EventDate, request.OrganizationId);
                    return null;
                }

                // Create event entity with settings
                var eventEntity = CreateEventEntity(request);

                // Persist to database
                var createdEventId = await SaveEventAsync(eventEntity);

                _logger.LogInformation("Event created successfully: {EventId} - {Name}", createdEventId, eventEntity.Name);

                // Return response with full details
                return await GetEventResponseAsync(createdEventId);
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
        private async Task<bool> IsDuplicateEventAsync(EventRequest request)
        {
            var eventRepo = _repository.GetRepository<Event>();

            Expression<Func<Event, bool>> duplicateExpression = e =>
                  e.Name == request.Name &&
                  e.EventDate.Date == request.EventDate.Date &&
                  e.OrganizationId == request.OrganizationId &&
                  e.AuditProperties.IsActive &&
                  !e.AuditProperties.IsDeleted;

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

            // Initialize audit properties
            var currentUserId = request.CreatedBy ?? 1; // TODO: Get from authentication context
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
    }
}
