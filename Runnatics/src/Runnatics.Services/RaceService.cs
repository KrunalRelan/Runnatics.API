using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
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
                               IUserContextService userContext) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IRacesService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger<RaceService> _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;

        public async Task<PagingList<RaceResponse>> Search(RaceSearchRequest request)
        {
            try
            {
                var tenantId = _userContext.TenantId;

                // Validate date range
                if (!ValidateDateRange(request))
                {
                    return [];
                }

                // Build and execute search query
                var searchResults = await ExecuteSearchQueryAsync(request, tenantId);

                // Project to DTOs (AutoMapper.ProjectTo) — Event collections are ignored in the mapping profile
                var raceResponses = await LoadRaceResponsesAsync(searchResults);

                // Create paging result
                var paging = new PagingList<RaceResponse>(raceResponses)
                {
                    TotalCount = searchResults.TotalCount
                };

                _logger.LogInformation("Race search completed for Event {EventId} by User {UserId}. Found {Count} races.",
               request.EventId, _userContext.UserId, paging.TotalCount);

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

        public async Task<bool> Create(RaceRequest request)
        {
            // Validate request
            if (!ValidateRaceRequest(request))
            {
                return false;
            }

            var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId :0;

            try
            {
                // Quick existence and tenant-scope check for parent Event
                var eventRepo = _repository.GetRepository<Event>();
                var parentEvent = await eventRepo.GetQuery(e =>
                        e.Id == request.EventId &&
                        e.AuditProperties.IsActive &&
                        !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (parentEvent == null)
                {
                    ErrorMessage = "Event not found or inactive.";
                    _logger.LogWarning("Race creation aborted - event not found or inactive. EventId: {EventId}, UserId: {UserId}", request.EventId, currentUserId);
                    return false;
                }

                await _repository.BeginTransactionAsync();

                try
                {
                    // Map DTO to domain entity and set service controlled fields
                    var race = _mapper.Map<Race>(request);
                    race.AuditProperties = CreateAuditProperties(currentUserId);

                    // If settings supplied, map and attach to the race so EF can persist in one SaveChanges
                    if (request.RaceSettings != null)
                    {
                        var settings = _mapper.Map<RaceSettings>(request.RaceSettings);
                        settings.AuditProperties = CreateAuditProperties(currentUserId);

                        // Attach via navigation so EF will set FK when saving
                        race.RaceSettings = settings;
                    }

                    var raceRepo = _repository.GetRepository<Race>();
                    await raceRepo.AddAsync(race);

                    // Single SaveChanges persists race and optional settings (EF will populate FK)
                    await _repository.SaveChangesAsync();

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
                        _logger.LogWarning(rbEx, "Rollback failed after error while creating race for EventId: {EventId}", request.EventId);
                    }

                    _logger.LogError(exInner, "Error creating race for EventId: {EventId}", request.EventId);
                    ErrorMessage = "Error creating race.";
                    return false;
                }
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error while creating race for EventId: {EventId}", request?.EventId);
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
                _logger.LogError(ex, "Unexpected error while creating race for EventId: {EventId}", request?.EventId);
                ErrorMessage = "An unexpected error occurred while creating the race.";
                return false;
            }
        }

        #region Helpers

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
        private async Task<Models.Data.Common.PagingList<Race>> ExecuteSearchQueryAsync(RaceSearchRequest request, int tenantId)
        {
            var raceRepo = _repository.GetRepository<Race>();
            var expression = BuildSearchExpression(request);
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
        private static Expression<Func<Race, bool>> BuildSearchExpression(RaceSearchRequest request)
        {
            return e =>
                e.EventId == request.EventId &&
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
            if (searchResults == null || searchResults.Count ==0)
            {
                return [];
            }

            var raceRepo = _repository.GetRepository<Race>();
            var orderedIds = searchResults.Select(e => e.Id).ToList();

            // Prepare base query for the relevant races
            var baseQuery = raceRepo.GetQuery(e => orderedIds.Contains(e.Id)).AsNoTracking();

            // Project to DTO. AutoMapper mapping must ignore Event collection properties.
            var projected = await baseQuery
                .ProjectTo<RaceResponse>(_mapper.ConfigurationProvider)
                .ToListAsync();

            // Restore original order based on orderedIds
            var ordered = orderedIds.Select(id => projected.First(p => p.Id == id)).ToList();
            return ordered;
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

        // Map client-facing property names to database property names
        private static readonly Dictionary<string, string> SortFieldMapping = new(StringComparer.OrdinalIgnoreCase)
        {
             { "CreatedAt", "AuditProperties.CreatedDate" },
             { "UpdatedAt", "AuditProperties.UpdatedDate" }
        };

        #endregion
    }
}