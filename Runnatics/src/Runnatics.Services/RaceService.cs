using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

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

        public async Task<bool> Create(RaceRequest request)
        {
            // Validate request
            if (!ValidateRaceRequest(request))
            {
                return false;
            }

            var currentUserId = _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

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

        private static AuditProperties CreateAuditProperties(int userId) =>
            new()
            {
                IsActive = true,
                IsDeleted = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = userId
            };

        #endregion
    }
}