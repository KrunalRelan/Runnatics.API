using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.API.Models.Requests;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.EventOrganizers;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class EventOrganizerService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<EventOrganizerService> logger,
        IConfiguration configuration,
        IUserContextService userContext) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventOrganizerService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger<EventOrganizerService> _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;

        public async Task<EventOrganizerResponse?> CreateEventOrganizerAsync(EventOrganizerRequest request)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                // Validate input
                if (request == null)
                {
                    ErrorMessage = "Request cannot be null.";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(request.EventOrganizerName))
                {
                    ErrorMessage = "Event organizer name is required.";
                    return null;
                }

                // Get event repository
                var eventRepo = _repository.GetRepository<EventOrganizer>();

                // Check if eventOrganizer exists and belongs to the organization
                var existingEventOrganizer = await eventRepo
                    .GetQuery(e => e.Name == request.EventOrganizerName
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingEventOrganizer != null)
                {
                    ErrorMessage = "Event Organizer already exists.";
                    _logger.LogError("Event Organizer already exists: with {request.EventOrganizerName}:", request.EventOrganizerName);
                    return null;
                }

                // Create new event organizer
                var createEventOrganizer = _mapper.Map<EventOrganizer>(request);
                createEventOrganizer.TenantId = tenantId;
                createEventOrganizer.AuditProperties = new AuditProperties
                {
                    CreatedBy = userId,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                };
                
                await eventRepo.AddAsync(createEventOrganizer);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer created for tenant: {TenantId} by user: {UserId}",
                    createEventOrganizer.TenantId, userId);

                var toReturn = _mapper.Map<EventOrganizerResponse>(createEventOrganizer);
                return toReturn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event organizer for tenant: {TenantId}", _userContext.TenantId);
                ErrorMessage = "Error creating event organizer.";
                return null;
            }
        }

        public async Task<EventOrganizerResponse?> GetEventOrganizerAsync(int id)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var eventRepo = _repository.GetRepository<EventOrganizer>();

                var eventOrganizer = await eventRepo
                    .GetQuery(eo => eo.Id == id
                              && eo.TenantId == tenantId
                              && !eo.AuditProperties.IsDeleted
                              && eo.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (eventOrganizer == null)
                {
                    ErrorMessage = "Event organizer not found.";
                    return null;
                }

                var toReturn = _mapper.Map<EventOrganizerResponse>(eventOrganizer);
                return toReturn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event organizer : {Id}", id);
                ErrorMessage = "Error retrieving event organizer.";
                return null;
            }
        }

        public async Task<string?> DeleteEventOrganizerAsync(int id)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var eventOrgRepo = _repository.GetRepository<EventOrganizer>();

                var existingOrgEvent = await eventOrgRepo
                    .GetQuery(e => e.Id == id
                        && e.TenantId == tenantId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingOrgEvent == null)
                {
                    ErrorMessage = "Event not found or does not belong to this tenant.";
                    _logger.LogError("Event not found: {EventId} for tenant: {TenantId}",
                        id, tenantId);
                    return null;
                }

                if (string.IsNullOrWhiteSpace(existingOrgEvent.Name))
                {
                    ErrorMessage = "No organizer assigned to this event.";
                    return null;
                }

                // Remove organizer name
                existingOrgEvent.Name = null;
                existingOrgEvent.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existingOrgEvent.AuditProperties.UpdatedBy = userId;

                await eventOrgRepo.UpdateAsync(existingOrgEvent);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer removed from event: {EventId} by user: {UserId}",
                    id, userId);

                return "Event organizer removed successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting event organizer for event: {EventId}", id);
                ErrorMessage = "Error deleting event organizer.";
                return null;
            }
        }

        public async Task<List<EventOrganizerResponse>?> GetAllEventOrganizersAsync()
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var eventOrgRepo = _repository.GetRepository<EventOrganizer>();

                var eventOrganizers = await eventOrgRepo
                    .GetQuery(eo => eo.TenantId == tenantId
                              && !eo.AuditProperties.IsDeleted
                              && eo.AuditProperties.IsActive)
                    .ToListAsync();

                var toReturn = _mapper.Map<List<EventOrganizerResponse>>(eventOrganizers);
                return toReturn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event organizers for tenant: {TenantId}", _userContext.TenantId);
                ErrorMessage = "Error retrieving event organizers.";
                return null;
            }
        }
    }
}
