using Runnatics.API.Models.Requests;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Interface for event organizer service operations
    /// </summary>
    public interface IEventOrganizerService
    {
        /// <summary>
        /// Gets the error message from the last operation
        /// </summary>
        string? ErrorMessage { get; }

        /// <summary>
        /// Create or update event organizer information
        /// </summary>
        /// <param name="request">Event organizer request</param>
        /// <param name="tenantId">Tenant ID</param>
        /// <param name="userId">User ID performing the action</param>
        /// <returns>Event organizer response or null if failed</returns>
        Task<EventOrganizerResponse?> CreateEventOrganizerAsync(EventOrganizerRequest request);

        /// <summary>
        /// Get event organizer by event ID
        /// </summary>
        /// <param name="eventId">Event ID</param>
        /// <param name="tenantId">Tenant ID</param>
        /// <returns>Event organizer response or null if not found</returns>
        Task<EventOrganizerResponse?> GetEventOrganizerAsync(int Id);

        /// <summary>
        /// Delete event organizer
        /// </summary>
        /// <param name="eventId">Event ID</param>
        /// <param name="tenantId">Tenant ID</param>
        /// <param name="userId">User ID performing the action</param>
        /// <returns>Success message or null if failed</returns>
        Task<string?> DeleteEventOrganizerAsync(int Id);

        Task<List<EventOrganizerResponse>?> GetAllEventOrganizersAsync();
    }
}

