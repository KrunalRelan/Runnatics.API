using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.API.Models.Requests;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class EventOrganizerService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IEventOrganizerService
    {
        protected readonly IMapper _mapper;
        protected readonly ILogger<EventOrganizerService> _logger;
        protected readonly IConfiguration _configuration;

        public EventOrganizerService(
            IUnitOfWork<RaceSyncDbContext> repository,
            IMapper mapper,
            ILogger<EventOrganizerService> logger,
            IConfiguration configuration) 
            : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<EventOrganizerResponse?> CreateEventOrganizerAsync(
            EventOrganizerRequest request, 
            Guid organizationId, 
            Guid userId)
        {
            try
            {
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
                var eventRepo = _repository.GetRepository<Event>();
                
                // Check if event exists and belongs to the organization
                var existingEvent = await eventRepo
                    .GetQuery(e => e.Id == request.EventId 
                        && e.OrganizationId == organizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingEvent == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    _logger.LogError("Event not found: {EventId} for organization: {OrganizationId}", 
                        request.EventId, organizationId);
                    return null;
                }

                // Check if organizer name is already set
                if (!string.IsNullOrWhiteSpace(existingEvent.OrganizerName))
                {
                    ErrorMessage = "Event already has an organizer assigned. Use update instead.";
                    _logger.LogWarning("Event {EventId} already has organizer: {OrganizerName}", 
                        existingEvent.Id, existingEvent.OrganizerName);
                    return null;
                }

                // Update event with organizer name
                existingEvent.OrganizerName = request.EventOrganizerName;
                existingEvent.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existingEvent.AuditProperties.UpdatedBy = userId;

                await eventRepo.UpdateAsync(existingEvent);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer created for event: {EventId} by user: {UserId}", 
                    existingEvent.Id, userId);

                return new EventOrganizerResponse
                {
                    EventId = existingEvent.Id,
                    EventName = existingEvent.Name,
                    OrganizerName = existingEvent.OrganizerName,
                    CreatedDate = existingEvent.AuditProperties.CreatedDate,
                    IsActive = existingEvent.AuditProperties.IsActive
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event organizer for event: {EventId}", request.EventId);
                ErrorMessage = "Error creating event organizer.";
                return null;
            }
        }

        public async Task<EventOrganizerResponse?> GetEventOrganizerAsync(Guid eventId, Guid organizationId)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                
                var eventEntity = await eventRepo
                    .GetQuery(e => e.Id == eventId 
                        && e.OrganizationId == organizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (eventEntity == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(eventEntity.OrganizerName))
                {
                    ErrorMessage = "No organizer assigned to this event.";
                    return null;
                }

                return new EventOrganizerResponse
                {
                    EventId = eventEntity.Id,
                    EventName = eventEntity.Name,
                    OrganizerName = eventEntity.OrganizerName,
                    CreatedDate = eventEntity.AuditProperties.CreatedDate,
                    IsActive = eventEntity.AuditProperties.IsActive
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event organizer for event: {EventId}", eventId);
                ErrorMessage = "Error retrieving event organizer.";
                return null;
            }
        }

        public async Task<EventOrganizerResponse?> UpdateEventOrganizerAsync(
            EventOrganizerRequest request, 
            Guid organizationId, 
            Guid userId)
        {
            try
            {
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
                var eventRepo = _repository.GetRepository<Event>();
                
                // Check if event exists and belongs to the organization
                var existingEvent = await eventRepo
                    .GetQuery(e => e.Id == request.EventId 
                        && e.OrganizationId == organizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingEvent == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    _logger.LogError("Event not found: {EventId} for organization: {OrganizationId}", 
                        request.EventId, organizationId);
                    return null;
                }

                // Update event with new organizer name
                existingEvent.OrganizerName = request.EventOrganizerName;
                existingEvent.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existingEvent.AuditProperties.UpdatedBy = userId;

                await eventRepo.UpdateAsync(existingEvent);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer updated for event: {EventId} by user: {UserId}", 
                    existingEvent.Id, userId);

                return new EventOrganizerResponse
                {
                    EventId = existingEvent.Id,
                    EventName = existingEvent.Name,
                    OrganizerName = existingEvent.OrganizerName,
                    CreatedDate = existingEvent.AuditProperties.CreatedDate,
                    IsActive = existingEvent.AuditProperties.IsActive
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating event organizer for event: {EventId}", request.EventId);
                ErrorMessage = "Error updating event organizer.";
                return null;
            }
        }

        public async Task<string?> DeleteEventOrganizerAsync(Guid eventId, Guid organizationId, Guid userId)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                
                var existingEvent = await eventRepo
                    .GetQuery(e => e.Id == eventId 
                        && e.OrganizationId == organizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingEvent == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    _logger.LogError("Event not found: {EventId} for organization: {OrganizationId}", 
                        eventId, organizationId);
                    return null;
                }

                if (string.IsNullOrWhiteSpace(existingEvent.OrganizerName))
                {
                    ErrorMessage = "No organizer assigned to this event.";
                    return null;
                }

                // Remove organizer name
                existingEvent.OrganizerName = null;
                existingEvent.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existingEvent.AuditProperties.UpdatedBy = userId;

                await eventRepo.UpdateAsync(existingEvent);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer removed from event: {EventId} by user: {UserId}", 
                    eventId, userId);

                return "Event organizer removed successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting event organizer for event: {EventId}", eventId);
                ErrorMessage = "Error deleting event organizer.";
                return null;
            }
        }
    }
}
