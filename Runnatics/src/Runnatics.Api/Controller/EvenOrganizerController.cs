using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.API.Models.Requests;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EventOrganizerController : ControllerBase
    {
        private readonly IEventOrganizerService _eventOrganizerService;

        public EventOrganizerController(IEventOrganizerService eventOrganizerService)
        {
            _eventOrganizerService = eventOrganizerService;
        }

        /// <summary>
        /// Create event organizer
        /// </summary>
        [HttpPost("create")]
        public async Task<IActionResult> CreateAsync([FromBody] EventOrganizerRequest request)
        {
            try
            {
                var userId = GetUserIdFromClaims();
                var organizationId = GetOrganizationIdFromClaims();

                if (userId == Guid.Empty || organizationId == Guid.Empty)
                {
                    return Unauthorized("Invalid user or organization claims.");
                }

                var result = await _eventOrganizerService.CreateEventOrganizerAsync(request, organizationId, userId);
                
                if (result == null)
                {
                    return BadRequest(new { error = _eventOrganizerService.ErrorMessage });
                }

                return Ok(result);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while creating event organizer." });
            }
        }

        /// <summary>
        /// Get event organizer by event ID
        /// </summary>
        [HttpGet("{eventId}")]
        public async Task<IActionResult> GetAsync(Guid eventId)
        {
            try
            {
                var organizationId = GetOrganizationIdFromClaims();

                if (organizationId == Guid.Empty)
                {
                    return Unauthorized("Invalid organization claims.");
                }

                var result = await _eventOrganizerService.GetEventOrganizerAsync(eventId, organizationId);
                
                if (result == null)
                {
                    return NotFound(new { error = _eventOrganizerService.ErrorMessage });
                }

                return Ok(result);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while retrieving event organizer." });
            }
        }

        /// <summary>
        /// Update event organizer
        /// </summary>
        [HttpPut("update")]
        public async Task<IActionResult> UpdateAsync([FromBody] EventOrganizerRequest request)
        {
            try
            {
                var userId = GetUserIdFromClaims();
                var organizationId = GetOrganizationIdFromClaims();

                if (userId == Guid.Empty || organizationId == Guid.Empty)
                {
                    return Unauthorized("Invalid user or organization claims.");
                }

                var result = await _eventOrganizerService.UpdateEventOrganizerAsync(request, organizationId, userId);
                
                if (result == null)
                {
                    return BadRequest(new { error = _eventOrganizerService.ErrorMessage });
                }

                return Ok(result);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while updating event organizer." });
            }
        }

        /// <summary>
        /// Delete event organizer
        /// </summary>
        [HttpDelete("{eventId}")]
        public async Task<IActionResult> DeleteAsync(Guid eventId)
        {
            try
            {
                var userId = GetUserIdFromClaims();
                var organizationId = GetOrganizationIdFromClaims();

                if (userId == Guid.Empty || organizationId == Guid.Empty)
                {
                    return Unauthorized("Invalid user or organization claims.");
                }

                var result = await _eventOrganizerService.DeleteEventOrganizerAsync(eventId, organizationId, userId);
                
                if (result == null)
                {
                    return BadRequest(new { error = _eventOrganizerService.ErrorMessage });
                }

                return Ok(new { message = result });
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while deleting event organizer." });
            }
        }

        // Helper methods to extract claims
        private Guid GetUserIdFromClaims()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value;
            
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }

        private Guid GetOrganizationIdFromClaims()
        {
            var orgIdClaim = User.FindFirst("organizationId")?.Value;
            
            return Guid.TryParse(orgIdClaim, out var orgId) ? orgId : Guid.Empty;
        }
    }
}
