using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing races 
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RacesController : ControllerBase
    {
        private readonly IRacesService _raceService;

        public RacesController(IRacesService raceService)
        {
            _raceService = raceService;
        }

        [HttpPost("search")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<PagingList<RaceResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Search([FromBody] RaceSearchRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Invalid input provided. Request body cannot be null." });
            }

            // Validate model state
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                          .SelectMany(v => v.Errors)
                          .Select(e => e.ErrorMessage)
                          .ToList()
                });
            }

            var response = new ResponseBase<PagingList<RaceResponse>>();
            var result = await _raceService.Search(request);

            if (_raceService.HasError)
            {
                response.Error = new ResponseBase<PagingList<RaceResponse>>.ErrorData()
                {
                    Message = _raceService.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            response.TotalCount = result.TotalCount;

            return Ok(response);
        }


        [HttpPost("create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] RaceRequest request)
        {
            if (request == null || request.EventId == 0)
            {
                return BadRequest(new { error = "Invalid input provided. Request body cannot be null." });
            }

            // Validate model state
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()
                });
            }

            await _raceService.Create(request);

            if (_raceService.HasError)
            {
                // Return 400 Bad Request for validation errors
                if (_raceService.ErrorMessage.Contains("cannot be in the past") ||
                    _raceService.ErrorMessage.Contains("cannot be before or equal to") ||
                    _raceService.ErrorMessage.Contains("required"))
                {
                    return BadRequest(_raceService.ErrorMessage);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, _raceService.ErrorMessage);
            }

            return Ok(HttpStatusCode.Created);
        }
    }
}
