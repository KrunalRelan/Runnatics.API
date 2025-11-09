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
                var organizationId = _userContext.OrganizationId;
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
                    .GetQuery(e => e.OrganizationId == request.OrganizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingEventOrganizer == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    _logger.LogError("Event not found: for organization: {OrganizationId}", request.OrganizationId);
                    return null;
                }

                // Check if organizer name is already set
                if (!string.IsNullOrWhiteSpace(existingEventOrganizer.OrganizerName))
                {
                    ErrorMessage = "Event Organizer already has an organizer assigned. Use update instead.";
                    _logger.LogWarning("Event Organizer already has organizer: {OrganizerName} for organization: {OrganizationId}",
                        existingEventOrganizer.OrganizerName, existingEventOrganizer.OrganizationId);
                    return null;
                }

                // Update event with organizer name
                existingEventOrganizer.OrganizerName = request.EventOrganizerName;
                existingEventOrganizer.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existingEventOrganizer.AuditProperties.UpdatedBy = userId;

                await eventRepo.UpdateAsync(existingEventOrganizer);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Event organizer created for event: {EventId} by user: {UserId}",
                    existingEventOrganizer.OrganizationId, userId);

                var toReturn = _mapper.Map<EventOrganizerResponse>(existingEventOrganizer);
                return toReturn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event organizer for event: {EventId}", request.OrganizationId);
                ErrorMessage = "Error creating event organizer.";
                return null;
            }
        }

        public async Task<EventOrganizerResponse?> GetEventOrganizerAsync(int id)
        {
            try
            {
                var organizationId = _userContext.OrganizationId;
                var userId = _userContext.UserId;

                var eventRepo = _repository.GetRepository<EventOrganizer>();

                var eventOrganizer = await eventRepo
                    .GetQuery(eo => eo.Id == id
                              && eo.OrganizationId == organizationId
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
                var organizationId = _userContext.OrganizationId;
                var userId = _userContext.UserId;

                var eventOrgRepo = _repository.GetRepository<EventOrganizer>();

                var existingOrgEvent = await eventOrgRepo
                    .GetQuery(e => e.Id == id
                        && e.OrganizationId == organizationId
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingOrgEvent == null)
                {
                    ErrorMessage = "Event not found or does not belong to this organization.";
                    _logger.LogError("Event not found: {EventId} for organization: {OrganizationId}",
                        id, organizationId);
                    return null;
                }

                if (string.IsNullOrWhiteSpace(existingOrgEvent.OrganizerName))
                {
                    ErrorMessage = "No organizer assigned to this event.";
                    return null;
                }

                // Remove organizer name
                existingOrgEvent.OrganizerName = null;
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
                var organizationId = _userContext.OrganizationId;
                var userId = _userContext.UserId;

                var eventOrgRepo = _repository.GetRepository<EventOrganizer>();

                var eventOrganizers = await eventOrgRepo
                    .GetQuery(eo => eo.OrganizationId == organizationId
                              && !eo.AuditProperties.IsDeleted
                              && eo.AuditProperties.IsActive)
                    .ToListAsync();

                var toReturn = _mapper.Map<List<EventOrganizerResponse>>(eventOrganizers);
                return toReturn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event organizers for organization: {OrganizationId}", _userContext.OrganizationId);
                ErrorMessage = "Error retrieving event organizers.";
                return null;
            }
        }
    }
}
