using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing race results and leaderboards
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ResultsController : ControllerBase
    {
        private readonly IResultsService _service;

        public ResultsController(IResultsService resultsService)
        {
            _service = resultsService;
        }

        /// <summary>
        /// Calculate split times for all participants at each checkpoint
        /// </summary>
        [HttpPost("{eventId}/{raceId}/calculate-splits")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<SplitTimeCalculationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CalculateSplitTimes(string eventId, string raceId, [FromBody] CalculateSplitTimesRequest? request = null)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required." });
            }

            // Create request if not provided
            request ??= new CalculateSplitTimesRequest
            {
                EventId = eventId,
                RaceId = raceId
            };

            // Override IDs from route
            request.EventId = eventId;
            request.RaceId = raceId;

            var response = new ResponseBase<SplitTimeCalculationResponse>();
            var result = await _service.CalculateSplitTimesAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<SplitTimeCalculationResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Calculate final results, rankings, and identify finishers
        /// </summary>
        [HttpPost("{eventId}/{raceId}/calculate-results")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<ResultsCalculationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CalculateResults(string eventId, string raceId, [FromBody] CalculateResultsRequest? request = null)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required." });
            }

            // Create request if not provided
            request ??= new CalculateResultsRequest
            {
                EventId = eventId,
                RaceId = raceId
            };

            // Override IDs from route
            request.EventId = eventId;
            request.RaceId = raceId;

            var response = new ResponseBase<ResultsCalculationResponse>();
            var result = await _service.CalculateResultsAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ResultsCalculationResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Get race leaderboard with filtering and pagination
        /// </summary>
        [HttpGet("{eventId}/{raceId}/leaderboard")]
        [ProducesResponseType(typeof(ResponseBase<LeaderboardResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetLeaderboard(
            string eventId,
            string raceId,
            [FromQuery] string rankBy = "overall",
            [FromQuery] string? gender = null,
            [FromQuery] string? category = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool includeSplits = false)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required." });
            }

            if (page < 1)
            {
                return BadRequest(new { error = "Page must be greater than 0." });
            }

            if (pageSize < 1 || pageSize > 100)
            {
                return BadRequest(new { error = "Page size must be between 1 and 100." });
            }

            var response = new ResponseBase<LeaderboardResponse>();
            var result = await _service.GetLeaderboardAsync(
                eventId,
                raceId,
                rankBy,
                gender,
                category,
                page,
                pageSize,
                includeSplits);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<LeaderboardResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Get detailed results for a specific participant
        /// </summary>
        [HttpGet("{eventId}/{raceId}/participant/{participantId}")]
        [ProducesResponseType(typeof(ResponseBase<ParticipantResultResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetParticipantResult(
            string eventId,
            string raceId,
            string participantId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(participantId))
            {
                return BadRequest(new { error = "Event ID, Race ID, and Participant ID are required." });
            }

            var response = new ResponseBase<ParticipantResultResponse>();
            var result = await _service.GetParticipantResultAsync(eventId, raceId, participantId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ParticipantResultResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };

                if (_service.ErrorMessage.Contains("not found"))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (result == null)
            {
                return NotFound(new { error = "Participant result not found." });
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
