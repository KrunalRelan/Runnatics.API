using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.CheckPoints;
using Runnatics.Models.Client.Responses.Checkpoints;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing checkpoints
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class CheckpointsController(ICheckpointsService checkpointsService) : ControllerBase
    {
        private readonly ICheckpointsService _checkpointsService = checkpointsService;

        /// <summary>
        /// Create a checkpoint for an event and race
        /// </summary>
        [HttpPost("{eventId}/{raceId}/create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create(string eventId, string raceId, [FromBody] CheckpointRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(eventId) || request == null)
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

            await _checkpointsService.Create(eventId, raceId, request);

            if (_checkpointsService.HasError)
            {
                // Return 400 Bad Request for validation errors
                // TODO

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, _checkpointsService.ErrorMessage);
            }

            return Ok(HttpStatusCode.Created);
        }

        /// <summary>
        /// Update a checkpoint
        /// </summary>
        [HttpPut("{eventId}/{raceId}/{id}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Update(string eventId, string raceId, string id, [FromBody] CheckpointRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(id) || request == null)
            {
                return BadRequest(new { error = "Invalid input provided." });
            }

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

            await _checkpointsService.Update(eventId, raceId, id, request);

            if (_checkpointsService.HasError)
            {
                // If the service indicates not found, return 404
                if (_checkpointsService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return NotFound(new { error = _checkpointsService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, _checkpointsService.ErrorMessage);
            }

            return NoContent();
        }

        /// <summary>
        /// Get a checkpoint by id
        /// </summary>
        [HttpGet("{eventId}/{raceId}/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Get(string eventId, string raceId, string id)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid identifiers provided." });
            }

            var checkpoint = await _checkpointsService.GetCheckpoint(eventId, raceId, id);

            if (_checkpointsService.HasError)
            {
                var response = new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData { Message = _checkpointsService.ErrorMessage }
                };

                if (!string.IsNullOrEmpty(_checkpointsService.ErrorMessage) &&
                    (_checkpointsService.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                     _checkpointsService.ErrorMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (checkpoint == null)
            {
                return NotFound(new { error = "Checkpoint not found." });
            }

            return Ok(checkpoint);
        }

        /// <summary>
        /// List all checkpoints for an event and race
        /// </summary>
        [HttpGet("{eventId}/{raceId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Search(string eventId, string raceId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Invalid identifiers provided." });
            }

            var response = new ResponseBase<PagingList<CheckpointResponse>>();
            var results = await _checkpointsService.Search(eventId, raceId);

            if (_checkpointsService.HasError)
            {
                response = new ResponseBase<PagingList<CheckpointResponse>>()
                {
                    Error = new ResponseBase<PagingList<CheckpointResponse>>.ErrorData { Message = _checkpointsService.ErrorMessage }
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = results;
            response.TotalCount = results.TotalCount;

            return Ok(response);
        }

        /// <summary>
        /// Delete a checkpoint
        /// </summary>
        [HttpDelete("{eventId}/{raceId}/{id}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(string eventId, string raceId, string id)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid identifiers provided." });
            }

            var response = new ResponseBase<object>();
            var result = await _checkpointsService.Delete(eventId, raceId, id);

            if (_checkpointsService.HasError)
            {
                response.Error = new ResponseBase<object>.ErrorData()
                {
                    Message = _checkpointsService.ErrorMessage
                };

                // Return 404 Not Found if checkpoint doesn't exist or unauthorized
                if (_checkpointsService.ErrorMessage.Contains("not found") ||
                       _checkpointsService.ErrorMessage.Contains("does not exist") ||
                  _checkpointsService.ErrorMessage.Contains("don't have permission"))
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
                    Message = "Checkpoint deletion failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = new { message = "Checkpoint deleted successfully", id };
            return Ok(response);
        }
    }
}
