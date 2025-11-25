using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Participant;
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
        private readonly IParticipantImportService _importService;

        public ParticipantsController(IParticipantImportService importService)
        {
            _importService = importService;
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

            var result = await _importService.UploadParticipantsCsvAsync(eventId, request);

            if (_importService.HasError)
            {
                response.Error = new ResponseBase<ParticipantImportResponse>.ErrorData
                {
                    Message = _importService.ErrorMessage
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

            var result = await _importService.ProcessStagingDataAsync(request);

            if (_importService.HasError)
            {
                response.Error = new ResponseBase<ProcessImportResponse>.ErrorData
                {
                    Message = _importService.ErrorMessage
                };

                if (_importService.ErrorMessage.Contains("not found"))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
