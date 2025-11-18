using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;
using Runnatics.Services;
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

        [HttpPost("{eventId}/search")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<PagingList<RaceResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Search(int eventId, [FromBody] RaceSearchRequest request)
        {
            if (eventId <= 0 || request == null)
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
            var result = await _raceService.Search(eventId, request);

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


        [HttpPost("{eventId}/create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create(int eventId, [FromBody] RaceRequest request)
        {
            if (eventId <= 0 || request == null)
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

            await _raceService.Create(eventId, request);

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

        [HttpGet("{eventId}/{raceId}/race-details")]
        public async Task<IActionResult> GetRace(int eventId, int raceId)
        {
            if (raceId <= 0)
            {
                return BadRequest(new { error = "Invalid race ID. ID must be greater than 0." });
            }

            var response = new ResponseBase<RaceResponse>();
            var result = await _raceService.GetRaceById(eventId, raceId);

            if (_raceService.HasError)
            {
                response.Error = new ResponseBase<RaceResponse>.ErrorData()
                {
                    Message = _raceService.ErrorMessage
                };

                // Return 404 Not Found if race doesn't exist
                if (_raceService.ErrorMessage.Contains("not found") ||
                    _raceService.ErrorMessage.Contains("does not exist"))
                {
                    return NotFound(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (result == null)
            {
                response.Error = new ResponseBase<RaceResponse>.ErrorData()
                {
                    Message = "Race retrieval failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            response.TotalCount = 1; //TODO

            return Ok(response);
        }

        [HttpPut("{eventId}/{id}/edit-race")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Update(int eventId, int id, [FromBody] RaceRequest request)
        {
            if (eventId <= 0 || id <= 0)
            {
                return BadRequest(new { error = "Invalid event ID or race ID. ID must be greater than 0." });
            }

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

            var result = await _raceService.Update(eventId, id, request);

            if (_raceService.HasError)
            {
                var response = new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData()
                    {
                        Message = _raceService.ErrorMessage
                    }
                };

                // Return 404 Not Found if race doesn't exist or unauthorized
                if (_raceService.ErrorMessage.Contains("not found") ||
                        _raceService.ErrorMessage.Contains("does not exist") ||
                        _raceService.ErrorMessage.Contains("don't have permission"))
                {
                    return NotFound(response);
                }

                // Return 409 Conflict for duplicate races
                if (_raceService.ErrorMessage.Contains("already exists"))
                {
                    return Conflict(response);
                }

                // Return 400 Bad Request for validation errors
                if (_raceService.ErrorMessage.Contains("cannot be in the past") ||
                        _raceService.ErrorMessage.Contains("cannot be null"))
                {
                    return BadRequest(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (!result)
            {
                var response = new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData()
                    {
                        Message = "Race update failed. Please try again."
                    }
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            return Ok(HttpStatusCode.OK);
        }

        [HttpDelete("{eventId}/{id}/delete-race")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(int eventId, int id)
        {
            if (eventId <= 0 || id <= 0)
            {
                return BadRequest(new { error = "Invalid race ID. ID must be greater than 0." });
            }

            var response = new ResponseBase<object>();
            var result = await _raceService.Delete(eventId, id);

            if (_raceService.HasError)
            {
                response.Error = new ResponseBase<object>.ErrorData()
                {
                    Message = _raceService.ErrorMessage
                };

                // Return 404 Not Found if race doesn't exist or unauthorized
                if (_raceService.ErrorMessage.Contains("not found") ||
                       _raceService.ErrorMessage.Contains("does not exist") ||
                  _raceService.ErrorMessage.Contains("don't have permission"))
                {
                    return NotFound(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (!result)
            {
                response.Error = new ResponseBase<object>.ErrorData()
                {
                    Message = "Race deletion failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = new { message = "Race deleted successfully", id };
            return Ok(response);
        }

    }
}
