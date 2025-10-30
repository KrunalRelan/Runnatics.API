using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController(IEventsService eventService) : ControllerBase
    {
        private readonly IEventsService _eventService = eventService;

        [HttpPost("create")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] EventRequest request)
        {
            if (request == null)
            {
                return BadRequest("Event details are not provided.");
            }

            await _eventService.CreateEventAsync(request);
            
            if (_eventService.HasError)
            {
                return BadRequest(_eventService.ErrorMessage);
            }

            return NoContent();
        }

    }
}
