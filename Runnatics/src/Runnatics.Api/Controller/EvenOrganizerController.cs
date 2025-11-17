using System.Net;
using System.Security.Claims;
using Azure;
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
    public class EventOrganizerController(IEventOrganizerService eventOrganizerService) : ControllerBase
    {
        private readonly IEventOrganizerService _eventOrganizerService = eventOrganizerService;

        /// <summary>
        /// Create event organizer
        /// </summary>
        [HttpPost("create-event-organizer")]
        public async Task<IActionResult> CreateAsync([FromBody] EventOrganizerRequest request)
        {
            try
            {
                var toReturn = new ResponseBase<EventOrganizerResponse>();
                var result = await _eventOrganizerService.CreateEventOrganizerAsync(request);

                if (result == null)
                {
                    return BadRequest(new { error = _eventOrganizerService.ErrorMessage });
                }

                toReturn.Message = result;
                
                return Ok(toReturn);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while creating event organizer." });
            }
        }

        /// <summary>
        /// Get event organizer by event ID
        /// </summary>
        [HttpGet("{id}/event-organizer")]
        public async Task<IActionResult> GetAsync(string id)
        {
            try
            {
                var result = await _eventOrganizerService.GetEventOrganizerAsync(id);

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
        /// Delete event organizer
        /// </summary>
        [HttpDelete("{id}/delete-event-organizer")]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            try
            {
                var result = await _eventOrganizerService.DeleteEventOrganizerAsync(id);

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

        /// <summary>
        /// Get all event organizers
        /// </summary>
        /// <returns></returns>
        [HttpGet("all-event-organizers")]
        public async Task<IActionResult> GetAllAsync()
        {
            try
            {
                var toReturn = new ResponseBase<List<EventOrganizerResponse>>();
                var result = await _eventOrganizerService.GetAllEventOrganizersAsync();

                if (result == null)
                {
                    return NotFound(new { error = _eventOrganizerService.ErrorMessage });
                }
                if (result.Count == 0)
                {
                    return NoContent();
                }

                toReturn.Message = result;
                return Ok(toReturn);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while retrieving event organizers." });
            }
        }
    }
}
