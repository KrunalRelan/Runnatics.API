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
            if (string.IsNullOrEmpty(eventId) ||string.IsNullOrEmpty(raceid)|| addParticipant is null)
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
            if (string.IsNullOrEmpty(participantId) || editParticipant is null )
            {
                return BadRequest();
            }

            await _service.EditParticipant(participantId, editParticipant);

            if (_service.HasError)
            {
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
                return StatusCode((int)HttpStatusCode.InternalServerError, _service.ErrorMessage);
            }

            else
                return Ok(HttpStatusCode.NoContent);
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
    }
}
