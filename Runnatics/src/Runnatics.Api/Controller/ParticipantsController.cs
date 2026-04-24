using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing participant imports
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ParticipantsController : ControllerBase
    {
        private readonly IParticipantImportService _service;

        public ParticipantsController(IParticipantImportService importService)
        {
            _service = importService;
        }

        /// <summary>
        /// Upload CSV file with participant data for staging
        /// </summary>
        [HttpPost("{eventId}/import")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<ParticipantImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadCsv(string eventId, [FromForm] ParticipantImportRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || request == null || request.File == null)
            {
                return BadRequest(new { error = "Invalid input provided. Event ID and file are required." });
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

            var response = new ResponseBase<ParticipantImportResponse>();

            var result = await _service.UploadParticipantsCsvAsync(eventId, request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ParticipantImportResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Process staged participant data and create participant records
        /// </summary>
        [HttpPost("{eventId}/import/{importBatchId}/process")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<ProcessImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessImport([FromBody] ProcessImportRequest request)
        {
            if (string.IsNullOrEmpty(request.EventId) || string.IsNullOrEmpty(request.ImportBatchId) || request == null)
            {
                return BadRequest(new { error = "Invalid input provided. Event ID, Import Batch ID, and request body are required." });
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

            var response = new ResponseBase<ProcessImportResponse>();

            var result = await _service.ProcessStagingDataAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ProcessImportResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };

                if (_service.ErrorMessage.Contains("not found"))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpPost("{eventId}/{raceId}/search")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<PagingList<EventResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Search([FromBody] ParticipantSearchRequest request, [FromRoute] string eventId, [FromRoute] string raceId)
        {
            if (request == null || string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Search request, eventId or raceId cannot be null." });
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

            var response = new ResponseBase<PagingList<ParticipantSearchReponse>>();
            var result = await _service.Search(request, eventId, raceId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<PagingList<ParticipantSearchReponse>>.ErrorData()
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            response.TotalCount = result.TotalCount;

            return Ok(response);
        }

        [HttpPost("{eventId}/{raceId}/add-participant")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddParticipant([FromRoute] string eventId, [FromRoute] string raceid, [FromBody] ParticipantRequest addParticipant)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceid) || addParticipant is null)
            {
                return BadRequest();
            }

            await _service.AddParticipant(eventId, raceid, addParticipant);

            if (_service.HasError)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, _service.ErrorMessage);
            }

            else
                return Ok(HttpStatusCode.Accepted);
        }

        [HttpPut("{participantId}/edit-participant")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> EditParticipant([FromRoute] string participantId, [FromBody] ParticipantRequest editParticipant)
        {
            if (string.IsNullOrEmpty(participantId) || editParticipant is null)
            {
                return BadRequest();
            }

            await _service.EditParticipant(participantId, editParticipant);

            if (_service.HasError)
            {
                if (_service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(_service.ErrorMessage);
                return StatusCode((int)HttpStatusCode.InternalServerError, _service.ErrorMessage);
            }

            else
                return Ok(HttpStatusCode.OK);
        }

        [HttpPut("{participantId}/delete-participant")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteParticipant([FromRoute] string participantId)
        {
            await _service.DeleteParicipant(participantId);

            if (_service.HasError)
            {
                if (_service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(_service.ErrorMessage);
                return StatusCode((int)HttpStatusCode.InternalServerError, _service.ErrorMessage);
            }

            else
                return Ok(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// Update a participant's details including run status, manual time, distance, and optional race reassignment.
        /// </summary>
        [HttpPut("~/api/races/{raceId}/participants/{participantId}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateParticipant(
            [FromRoute] string raceId,
            [FromRoute] string participantId,
            [FromBody] UpdateParticipantRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(participantId) || request is null)
                return BadRequest(new { error = "Race ID, Participant ID, and request body are required." });

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

            var updated = await _service.UpdateParticipantExtendedAsync(raceId, participantId, request);

            if (_service.HasError)
            {
                if (_service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { error = _service.ErrorMessage });
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _service.ErrorMessage });
            }

            return Ok(new ResponseBase<ParticipantSearchReponse> { Message = updated });
        }

        /// <summary>
        /// Soft-delete a participant by race and participant ID.
        /// </summary>
        [HttpDelete("~/api/races/{raceId}/participants/{participantId}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteParticipantByRace(
            [FromRoute] string raceId,
            [FromRoute] string participantId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(participantId))
                return BadRequest(new { error = "Race ID and Participant ID are required." });

            await _service.DeleteParticipantAsync(raceId, participantId);

            if (_service.HasError)
            {
                if (_service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { error = _service.ErrorMessage });
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _service.ErrorMessage });
            }

            return NoContent();
        }

        /// <summary>
        /// Export all participants for a race as an xlsx file with dynamic checkpoint columns.
        /// </summary>
        [HttpGet("~/api/races/{raceId}/participants/export")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportParticipants([FromRoute] string raceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(raceId))
                return BadRequest(new { error = "Race ID is required." });

            var bytes = await _service.ExportParticipantsAsync(raceId);

            if (_service.HasError || bytes == null)
                return StatusCode((int)System.Net.HttpStatusCode.InternalServerError, new { error = _service.ErrorMessage });

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"participants_race_{raceId}.xlsx");
        }

        /// <summary>
        /// Export all participants with full race details: ranking, chip/gun times, SMS sent time, and absolute checkpoint clock times.
        /// </summary>
        [HttpGet("{eventId}/{raceId}/export")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportParticipantsDetailed([FromRoute] string eventId, [FromRoute] string raceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
                return BadRequest(new { error = "Event ID and Race ID are required." });

            var bytes = await _service.ExportParticipantsDetailedAsync(eventId, raceId);

            if (_service.HasError || bytes == null)
                return StatusCode((int)System.Net.HttpStatusCode.InternalServerError, new { error = _service.ErrorMessage });

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"participants_export_{raceId}.xlsx");
        }

        [HttpGet("{eventId}/{raceId}/categories")]
        public async Task<IActionResult> Categories([FromRoute] string eventId, [FromRoute] string raceId)
        {
            var response = new ResponseBase<List<Category>>();
            var result = await _service.GetCategories(eventId, raceId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<List<Category>>.ErrorData()
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Get detailed participant information including performance, rankings, split times and pace progression
        /// </summary>
        [HttpGet("{eventId}/{raceId}/participant/{participantId}/details")]
        [ProducesResponseType(typeof(ResponseBase<ParticipantDetailsResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetParticipantDetails(
            [FromRoute] string eventId,
            [FromRoute] string raceId,
            [FromRoute] string participantId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(participantId))
            {
                return BadRequest(new { error = "Event ID, Race ID, and Participant ID are required." });
            }

            var response = new ResponseBase<ParticipantDetailsResponse>();
            var result = await _service.GetParticipantDetails(eventId, raceId, participantId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ParticipantDetailsResponse>.ErrorData()
                {
                    Message = _service.ErrorMessage
                };

                if (_service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (result == null)
            {
                return NotFound(new { error = "Participant not found." });
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Add participants with bib numbers in a specified range
        /// </summary>
        [HttpPost("{eventId}/{raceId}/add-participant-range")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<AddParticipantRangeResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddParticipantRange(
            [FromRoute] string eventId,
            [FromRoute] string raceId,
            [FromBody] AddParticipantRangeRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || request is null)
            {
                return BadRequest(new { error = "Event ID, Race ID, and request body are required." });
            }

            if (request.FromBibNumber > request.ToBibNumber)
            {
                return BadRequest(new { error = "From Bib Number must be less than or equal to To Bib Number." });
            }

            // Limit the range to prevent excessive record creation
            const int maxRangeSize = 10000;
            if (request.ToBibNumber - request.FromBibNumber + 1 > maxRangeSize)
            {
                return BadRequest(new { error = $"Range cannot exceed {maxRangeSize} bib numbers." });
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

            var response = new ResponseBase<AddParticipantRangeResponse>();
            var result = await _service.AddParticipantRangeAsync(eventId, raceId, request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<AddParticipantRangeResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Update existing participants by matching bib numbers from uploaded CSV file
        /// </summary>
        /// <remarks>
        /// Use this endpoint when participants were created using AddParticipantRange (with only bib numbers)
        /// and you now want to update them with full details (name, email, phone, etc.) from a CSV file.
        /// The CSV should contain a BIB column that matches existing participant bib numbers.
        /// Only matching bib numbers will be updated; non-matching bibs are reported in the response.
        /// </remarks>
        [HttpPost("{eventId}/{raceId}/update-by-bib")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<UpdateParticipantsByBibResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateParticipantsByBib(
            [FromRoute] string eventId,
            [FromRoute] string raceId,
            [FromForm] UpdateParticipantsByBibRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required." });
            }

            if (request?.File == null || request.File.Length == 0)
            {
                return BadRequest(new { error = "CSV file is required." });
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

            var response = new ResponseBase<UpdateParticipantsByBibResponse>();
            var result = await _service.UpdateParticipantsByBibAsync(eventId, raceId, request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<UpdateParticipantsByBibResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
