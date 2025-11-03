using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    public class EventsService(IUnitOfWork<RaceSyncDbContext> repository,
                                IMapper mapper,
                                ILogger<AuthenticationService> logger,
                                IConfiguration configuration) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventsService
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

                var data = await eventRepo.SearchAsync(expression,
                                                    request.PageSize,
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
                return new PagingList<EventResponse>(); // Return empty list instead of null
            }
        }

        public async Task<EventResponse?> Create(EventRequest request)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                // Check for existing event with same name and date, and is active/not deleted
                Expression<Func<Event, bool>> expression = e =>
                    e.Name == request.Name &&
                    e.EventDate == request.EventDate &&
                    e.OrganizationId == request.OrganizationId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted;

                // Use AsNoTracking to prevent DataReader conflicts
                var alreadyExists = await eventRepo.GetQuery(expression)
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync();
                
                if (alreadyExists != null)
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    _logger.LogWarning("Duplicate event creation attempt: {Name} on {Date}", request.Name, request.EventDate);
                    return null;
                }

                // Map request to entity
                var eventEntity = _mapper.Map<Event>(request);

                // Set audit properties
                eventEntity.AuditProperties = eventEntity.AuditProperties ?? new Models.Data.Common.AuditProperties();
                eventEntity.AuditProperties.IsActive = true;
                eventEntity.AuditProperties.IsDeleted = false;
                eventEntity.AuditProperties.CreatedDate = DateTime.UtcNow;
                eventEntity.AuditProperties.CreatedBy = request.CreatedBy ?? 1; // ToDo : Replace with actual user ID from context

                // Handle EventSettings if provided
                if (request.EventSettings != null)
                {
                    var eventSettings = _mapper.Map<EventSettings>(request.EventSettings);
                    eventSettings.AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        IsActive = true,
                        IsDeleted = false,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = request.CreatedBy ?? 1
                    };
                    eventEntity.EventSettings = eventSettings;
                }

                // Handle LeaderboardSettings if provided
                if (request.LeaderboardSettings != null)
                {
                    var leaderboardSettings = _mapper.Map<Models.Data.Entities.LeaderboardSettings>(request.LeaderboardSettings);
                    leaderboardSettings.AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        IsActive = true,
                        IsDeleted = false,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = request.CreatedBy ?? 1
                    };
                    eventEntity.LeaderboardSettings = leaderboardSettings;
                }

                // Add and save
                await eventRepo.AddAsync(eventEntity);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event created successfully: {EventId} - {Name}", eventEntity.Id, eventEntity.Name);

                // Reload entity with EventSettings and LeaderboardSettings to ensure mapping works correctly
                var createdEvent = await eventRepo.GetQuery(e => e.Id == eventEntity.Id)
                    .Include(e => e.EventSettings)
                    .Include(e => e.LeaderboardSettings)
                    .Include(e => e.Organization)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                // Map to response
                var toReturn = _mapper.Map<EventResponse>(createdEvent ?? eventEntity); 
                return toReturn;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error during event creation for: {Name}", request.Name);
                return null;
            }
        }
    }
}
