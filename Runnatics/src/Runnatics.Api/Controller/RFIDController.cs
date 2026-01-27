using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing RFID imports and readings
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RFIDController : ControllerBase
    {
        private readonly IRFIDImportService _service;

        public RFIDController(IRFIDImportService importService)
        {
            _service = importService;
        }

        /// <summary>
        /// Upload EPC-BIB mapping Excel file
        /// </summary>
        [HttpPost("{eventId}/{raceId}/epc-mapping")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<EPCMappingImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadEPCMapping(string eventId, string raceId, [FromForm] EPCMappingImportRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || request == null || request.File == null)
            {
                return BadRequest(new { error = "Invalid input provided. Event ID, Race ID, and file are required." });
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

            var response = new ResponseBase<EPCMappingImportResponse>();
            var result = await _service.UploadEPCMappingAsync(eventId, raceId, request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<EPCMappingImportResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Upload SQLite database file with RFID readings
        /// </summary>
        [HttpPost("{eventId}/{raceId}/import")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<RFIDImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadRFIDFile(string eventId, string raceId, [FromForm] RFIDImportRequest request)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId) || request == null || request.File == null)
            {
                return BadRequest(new { error = "Invalid input provided. Event ID, Race ID, and file are required." });
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

            var response = new ResponseBase<RFIDImportResponse>();
            var result = await _service.UploadRFIDFileAsync(eventId, raceId, request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<RFIDImportResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Upload SQLite database file with RFID readings using auto-detection.
        /// Automatically determines event and race based on device name extracted from filename.
        /// </summary>
        [HttpPost("import-auto")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<RFIDImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadRFIDFileAuto([FromForm] RFIDImportRequest request)
        {
            if (request == null || request.File == null)
            {
                return BadRequest(new { error = "File is required." });
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

            var response = new ResponseBase<RFIDImportResponse>();
            var result = await _service.UploadRFIDFileAutoAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<RFIDImportResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Process uploaded RFID readings and link to participants
        /// </summary>
        [HttpPost("{eventId}/{raceId}/import/{uploadBatchId}/process")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<ProcessRFIDImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessImport([FromBody] ProcessRFIDImportRequest request)
        {
            if (string.IsNullOrEmpty(request.EventId) || 
                string.IsNullOrEmpty(request.RaceId) || 
                string.IsNullOrEmpty(request.UploadBatchId))  // Changed from ImportBatchId
            {
                return BadRequest(new { error = "Invalid input provided. Event ID, Race ID, and Upload Batch ID are required." });
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

            var response = new ResponseBase<ProcessRFIDImportResponse>();
            var result = await _service.ProcessRFIDStagingDataAsync(request);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<ProcessRFIDImportResponse>.ErrorData
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

        /// <summary>
        /// Deduplicate RFID readings and populate normalized reading table
        /// </summary>
        [HttpPost("{eventId}/{raceId}/deduplicate")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<DeduplicationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Deduplicate(string eventId, string raceId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Invalid input provided. Event ID and Race ID are required." });
            }

            var response = new ResponseBase<DeduplicationResponse>();
            var result = await _service.DeduplicateAndNormalizeAsync(eventId, raceId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<DeduplicationResponse>.ErrorData
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
