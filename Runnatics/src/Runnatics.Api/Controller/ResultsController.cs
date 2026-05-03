using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Services.Interface;
using System.Net;
using System.Text;

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
        private readonly IResultsExportService _exportService;

        public ResultsController(IResultsService resultsService, IResultsExportService exportService)
        {
            _service = resultsService;
            _exportService = exportService;
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
        [HttpPost("leaderboard")]
        [ProducesResponseType(typeof(ResponseBase<LeaderboardResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetLeaderboard([FromBody] GetLeaderboardRequest request)
        {
            if (string.IsNullOrEmpty(request.EventId) || string.IsNullOrEmpty(request.RaceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required." });
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

            var response = new ResponseBase<LeaderboardResponse>();
            var result = await _service.GetLeaderboardAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<LeaderboardResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };

                if (_service.ErrorMessage.Contains("not enabled") || _service.ErrorMessage.Contains("not published"))
                {
                    return StatusCode((int)HttpStatusCode.Forbidden, response);
                }

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

        /// <summary>
        /// Get comprehensive participant details including performance, rankings, split times, and RFID readings
        /// </summary>
        [HttpGet("{eventId}/{raceId}/participant/{participantId}/details")]
        [ProducesResponseType(typeof(ResponseBase<ParticipantDetailsResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetParticipantDetails(
            string eventId,
            string raceId,
            string participantId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(participantId))
            {
                return BadRequest(new { error = "Event ID, Race ID, and Participant ID are required." });
            }

            var response = new ResponseBase<ParticipantDetailsResponse>();
            var result = await _service.GetParticipantDetailsAsync(eventId, raceId, participantId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ParticipantDetailsResponse>.ErrorData
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
                return NotFound(new { error = "Participant details not found." });
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Export race results as CSV including all participant fields and checkpoint split times.
        /// </summary>
        [HttpGet("{eventId}/{raceId}/export")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportResults(
            string eventId,
            string raceId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
                return BadRequest(new { error = "Event ID and Race ID are required." });

            // Use the leaderboard request to fetch all results (max page size)
            var request = new GetLeaderboardRequest
            {
                EventId = eventId,
                RaceId = raceId,
                PageNumber = 1,
                PageSize = 10000,
                IncludeSplits = true
            };

            var leaderboard = await _service.GetLeaderboardAsync(request);

            if (_service.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _service.ErrorMessage });

            if (leaderboard == null || leaderboard.Results == null || leaderboard.Results.Count == 0)
                return NotFound(new { error = "No results found for this race." });

            // Collect all checkpoint names across all entries
            var checkpointNames = leaderboard.Results
                .Where(e => e.Splits != null)
                .SelectMany(e => e.Splits!.Select(s => s.CheckpointName))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Build CSV
            var csv = new StringBuilder();

            // Header row
            var headers = new List<string>
            {
                "BibNumber", "Name", "Email", "Mobile", "Gender",
                "AgeCategory", "Status", "GunTime", "ChipTime",
                "OverallRank", "GenderRank", "CategoryRank"
            };
            headers.AddRange(checkpointNames);
            csv.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));

            // Data rows
            foreach (var entry in leaderboard.Results)
            {
                var splitLookup = entry.Splits?
                    .Where(s => !string.IsNullOrEmpty(s.CheckpointName))
                    .ToDictionary(s => s.CheckpointName!, s => s.SplitTime)
                    ?? new Dictionary<string, string?>();

                var row = new List<string?>
                {
                    entry.Bib,
                    entry.FullName,
                    entry.Email,
                    entry.Phone,
                    entry.Gender,
                    entry.Category,
                    entry.Status,
                    entry.GunTime,
                    entry.NetTime,
                    entry.OverallRank?.ToString(),
                    entry.GenderRank?.ToString(),
                    entry.CategoryRank?.ToString()
                };

                foreach (var cp in checkpointNames)
                    row.Add(splitLookup.TryGetValue(cp, out var time) ? time : null);

                csv.AppendLine(string.Join(",", row.Select(v => EscapeCsvField(v ?? ""))));
            }

            var fileName = $"results_export_{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }

        /// <summary>
        /// Export race results as Excel (.xlsx), honouring all leaderboard display settings.
        /// Columns, split times, pace, and rank views are driven by the configured settings.
        /// </summary>
        [HttpGet("{eventId}/{raceId}/export-excel")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportResultsExcel(
            string eventId,
            string raceId,
            [FromQuery] string rankBy = "Overall",
            [FromQuery] string? gender = null,
            [FromQuery] string? category = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
                return BadRequest(new { error = "Event ID and Race ID are required." });

            var request = new GetLeaderboardRequest
            {
                EventId = eventId,
                RaceId = raceId,
                RankBy = rankBy,
                Gender = string.IsNullOrWhiteSpace(gender) ? null : gender,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                PageNumber = 1,
                PageSize = 10000,
                IncludeSplits = true,
            };

            var result = await _exportService.ExportResultsExcelAsync(request, cancellationToken);

            if (result is null)
                return NotFound(new { error = "No results found for this race." });

            return File(result.Content, result.ContentType, result.FileName);
        }

        private static string EscapeCsvField(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
