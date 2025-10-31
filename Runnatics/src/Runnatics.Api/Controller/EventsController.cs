using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController(IEventsService eventService) : ControllerBase
    {
        private readonly IEventsService _eventService = eventService;

        [HttpPost("search")]
        [Authorize(Roles = "Admin")]

        public async Task<IActionResult> Search([FromBody] EventSearchRequest request)
        {
            var response = new ResponseBase<PagingList<EventResponse>>();
            var result = await _eventService.Search(request);

            if (_eventService.HasError)
            {
                response.Error = new ResponseBase<PagingList<EventResponse>>.ErrorData() { Message = _eventService.ErrorMessage };
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            response.Message = result;
            response.Message.TotalCount = result.TotalCount;

            return Ok(response);
        }

        [HttpPost("create")]
        //[Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] EventRequest request)
        {
            if (request == null)
            {
                return BadRequest("Invalid Input Provided.");
            }

            var response = new ResponseBase<EventResponse>();
            var result = await _eventService.Create(request);

            if (_eventService.HasError)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            response.Message = result;
            return StatusCode((int)HttpStatusCode.Created, response);
        }

    }
}
