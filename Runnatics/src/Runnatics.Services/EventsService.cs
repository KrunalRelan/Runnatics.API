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

                var data = await eventRepo.SearchAsync(expression, request.PageSize,
                          request.PageNumber,
                          request.SortDirection == SortDirection.Ascending ?
                          Models.Data.Common.SortDirection.Ascending :
                          Models.Data.Common.SortDirection.Descending,
                          false,
                          request.SortFieldName,
                          false);

                // Manually load only the navigation properties we need
                var eventsWithDetails = await eventRepo.GetQuery(e => data.Select(d => d.Id).Contains(e.Id))
                    .Include(e => e.EventSettings)
                    .Include(e => e.LeaderboardSettings)
                    .Include(e => e.Organization)
                    .Include(e => e.EventOrganizer)
                    .AsNoTracking()
                    .ToListAsync();

                var mappedData = _mapper.Map<PagingList<EventResponse>>(eventsWithDetails);
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

                // Validate request
                if (!ValidateEventRequest(request))
                {
                    return null;
                }

                // Check for duplicates
                if (await IsDuplicateEventAsync(request, organizationId))
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
        private async Task<bool> IsDuplicateEventAsync(EventRequest request, int organizationId)
        {
            var eventRepo = _repository.GetRepository<Event>();

            Expression<Func<Event, bool>> duplicateExpression = e =>
                e.Name == request.Name &&
                e.EventDate.Date == request.EventDate.Date &&
                e.OrganizationId == organizationId &&
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

            // Get current user ID and organization ID from context
            var currentUserId = _userContext.UserId;
            var organizationId = _userContext.OrganizationId;
            
            // Set the organization ID from the JWT token
            eventEntity.OrganizationId = organizationId;
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
            eventSettings.Id = 0; // Ensure EF Core generates a new ID
            eventSettings.EventId = 0; // Will be set after Event is saved
            eventSettings.AuditProperties = CreateAuditProperties(userId);
            return eventSettings;
        }

        /// <summary>
        /// Creates leaderboard settings entity
        /// </summary>
        private LeaderboardSettings CreateLeaderboardSettings(LeaderboardSettingsRequest request, int userId)
        {
            var leaderboardSettings = _mapper.Map<LeaderboardSettings>(request);
            leaderboardSettings.Id = 0; // Ensure EF Core generates a new ID
            leaderboardSettings.EventId = 0; // Will be set after Event is saved
            leaderboardSettings.AuditProperties = CreateAuditProperties(userId);
            return leaderboardSettings;
        }

        /// <summary>
        /// Saves the event entity to the database
        /// </summary>
        private async Task<int> SaveEventAsync(Event eventEntity)
        {
            try
            {
                _logger.LogInformation("Saving event: {EventName}, HasEventSettings: {HasEventSettings}, HasLeaderboardSettings: {HasLeaderboardSettings}", 
                    eventEntity.Name, 
                    eventEntity.EventSettings != null, 
                    eventEntity.LeaderboardSettings != null);
                
                // Store references to child entities and their data
                EventSettings? eventSettingsData = null;
                LeaderboardSettings? leaderboardSettingsData = null;
                
                if (eventEntity.EventSettings != null)
                {
                    eventSettingsData = eventEntity.EventSettings;
                    eventEntity.EventSettings = null; // Detach from event
                }
                
                if (eventEntity.LeaderboardSettings != null)
                {
                    leaderboardSettingsData = eventEntity.LeaderboardSettings;
                    eventEntity.LeaderboardSettings = null; // Detach from event
                }
                
                // Step 1: Save the Event entity first
                var eventRepo = _repository.GetRepository<Event>();
                var addedEvent = await eventRepo.AddAsync(eventEntity);
                await _repository.SaveChangesAsync();
                
                _logger.LogInformation("Event saved with ID: {EventId}", addedEvent.Id);
                
                // Step 2: Now save EventSettings if it exists - create a new instance to avoid tracking issues
                if (eventSettingsData != null)
                {
                    var newEventSettings = new EventSettings
                    {
                        EventId = addedEvent.Id,
                        RemoveBanner = eventSettingsData.RemoveBanner,
                        Published = eventSettingsData.Published,
                        RankOnNet = eventSettingsData.RankOnNet,
                        ShowResultSummaryForRaces = eventSettingsData.ShowResultSummaryForRaces,
                        UseOldData = eventSettingsData.UseOldData,
                        ConfirmedEvent = eventSettingsData.ConfirmedEvent,
                        AuditProperties = eventSettingsData.AuditProperties
                    };
                    
                    var settingsRepo = _repository.GetRepository<EventSettings>();
                    await settingsRepo.AddAsync(newEventSettings);
                    _logger.LogInformation("EventSettings prepared for Event ID: {EventId}", addedEvent.Id);
                }
                
                // Step 3: Now save LeaderboardSettings if it exists - create a new instance to avoid tracking issues
                if (leaderboardSettingsData != null)
                {
                    var newLeaderboardSettings = new LeaderboardSettings
                    {
                        EventId = addedEvent.Id,
                        ShowOverallResults = leaderboardSettingsData.ShowOverallResults,
                        ShowCategoryResults = leaderboardSettingsData.ShowCategoryResults,
                        ShowGenderResults = leaderboardSettingsData.ShowGenderResults,
                        ShowAgeGroupResults = leaderboardSettingsData.ShowAgeGroupResults,
                        SortByOverallChipTime = leaderboardSettingsData.SortByOverallChipTime,
                        SortByOverallGunTime = leaderboardSettingsData.SortByOverallGunTime,
                        SortByCategoryChipTime = leaderboardSettingsData.SortByCategoryChipTime,
                        SortByCategoryGunTime = leaderboardSettingsData.SortByCategoryGunTime,
                        EnableLiveLeaderboard = leaderboardSettingsData.EnableLiveLeaderboard,
                        ShowSplitTimes = leaderboardSettingsData.ShowSplitTimes,
                        ShowPace = leaderboardSettingsData.ShowPace,
                        ShowTeamResults = leaderboardSettingsData.ShowTeamResults,
                        ShowMedalIcon = leaderboardSettingsData.ShowMedalIcon,
                        AllowAnonymousView = leaderboardSettingsData.AllowAnonymousView,
                        AutoRefreshIntervalSec = leaderboardSettingsData.AutoRefreshIntervalSec,
                        MaxDisplayedRecords = leaderboardSettingsData.MaxDisplayedRecords,
                        NumberOfResultsToShowOverall = leaderboardSettingsData.NumberOfResultsToShowOverall,
                        NumberOfResultsToShowCategory = leaderboardSettingsData.NumberOfResultsToShowCategory,
                        AuditProperties = leaderboardSettingsData.AuditProperties
                    };
                    
                    var leaderboardRepo = _repository.GetRepository<LeaderboardSettings>();
                    await leaderboardRepo.AddAsync(newLeaderboardSettings);
                    _logger.LogInformation("LeaderboardSettings prepared for Event ID: {EventId}", addedEvent.Id);
                }
                
                // Step 4: Save all child entities
                if (eventSettingsData != null || leaderboardSettingsData != null)
                {
                    await _repository.SaveChangesAsync();
                    _logger.LogInformation("Event settings saved successfully for Event ID: {EventId}", addedEvent.Id);
                }
                
                return addedEvent.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving event: {EventName}. Error: {Error}", eventEntity.Name, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Retrieves the complete event response with all related data
        /// </summary>
        private async Task<EventResponse?> GetEventResponseAsync(int eventId)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                _logger.LogInformation("Attempting to retrieve event with ID: {EventId}", eventId);

                // First, check if the event exists at all
                var eventExists = await eventRepo.GetQuery(e => e.Id == eventId, ignoreQueryFilters: true).AnyAsync();
                _logger.LogInformation("Event exists in database: {Exists} for ID: {EventId}", eventExists, eventId);

                if (!eventExists)
                {
                    this.ErrorMessage = "Event was created but could not be found in database.";
                    _logger.LogError("Event with ID {EventId} does not exist in database", eventId);
                    return null;
                }

                // Now retrieve with all includes
                var createdEvent = await eventRepo.GetQuery(e => e.Id == eventId, ignoreQueryFilters: true)
                                        .Include(e => e.EventSettings)
                                        .Include(e => e.LeaderboardSettings)
                                        .Include(e => e.Organization)
                                        .Include(e => e.EventOrganizer)
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync();

                if (createdEvent == null)
                {
                    this.ErrorMessage = "Event found but could not be retrieved with navigation properties.";
                    _logger.LogError("Failed to retrieve event with includes for ID: {EventId}", eventId);
                    return null;
                }

                _logger.LogInformation("Event retrieved successfully: {EventId}, Name: {EventName}, HasSettings: {HasSettings}, HasLeaderboard: {HasLeaderboard}, HasOrganization: {HasOrg}, HasOrganizer: {HasOrganizer}", 
                    createdEvent.Id, 
                    createdEvent.Name,
                    createdEvent.EventSettings != null,
                    createdEvent.LeaderboardSettings != null,
                    createdEvent.Organization != null,
                    createdEvent.EventOrganizer != null);

                var mappedResponse = _mapper.Map<EventResponse>(createdEvent);
                
                _logger.LogInformation("Event mapped to response successfully for ID: {EventId}", eventId);
                
                return mappedResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event with ID: {EventId}. Error: {Error}", eventId, ex.Message);
                this.ErrorMessage = $"Error retrieving event: {ex.Message}";
                throw;
            }
        }

        #endregion
    }
}
