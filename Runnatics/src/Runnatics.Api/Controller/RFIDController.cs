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
        /// Upload EPC-BIB mapping Excel file.
        /// RaceId is optional - when not provided, mappings will apply to all participants across all races of the event.
        /// </summary>
        [HttpPost("{eventId}/epc-mapping")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<EPCMappingImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadEPCMapping(string eventId, [FromQuery] string? raceId, [FromForm] EPCMappingImportRequest request)
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
        /// Upload SQLite database file with RFID readings at event level.
        /// RaceId is optional - when not provided, the file is stored at event level and race association 
        /// happens during processing via EPC → Participant → RaceId. This is the recommended approach 
        /// when a single device captures data for multiple races (e.g., same mat used for 21KM and 5KM).
        /// </summary>
        /// <param name="eventId">Encrypted event ID</param>
        /// <param name="raceId">Optional encrypted race ID. If null, file applies to all races.</param>
        /// <param name="request">The RFID file upload request</param>
        [HttpPost("{eventId}/import")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<RFIDImportResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadRFIDFileEventLevel(
            string eventId,
            [FromQuery] string? raceId,
            [FromForm] RFIDImportRequest request)
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

            var response = new ResponseBase<RFIDImportResponse>();
            var result = await _service.UploadRFIDFileEventLevelAsync(eventId, raceId, request);

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
        /// Complete RFID processing workflow: Process all pending batches, deduplicate readings, and calculate results.
        /// This is a convenience endpoint that runs all three phases in sequence for maximum efficiency.
        /// </summary>
        /// <param name="forceReprocess">If true, clears all processed data before reprocessing. Use after checkpoint mapping changes.</param>
        [HttpPost("{eventId}/{raceId}/process-all")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<CompleteRFIDProcessingResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessAllBatches(
            string eventId,
            string raceId,
            [FromQuery] bool forceReprocess = false)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Invalid input provided. Event ID and Race ID are required." });
            }

            var response = new ResponseBase<CompleteRFIDProcessingResponse>();

            // If force reprocess requested, clear existing data first
            if (forceReprocess)
            {
                var clearResult = await _service.ClearProcessedDataAsync(eventId, raceId, keepUploads: true);
                if (clearResult.Status != "Success")
                {
                    response.Error = new ResponseBase<CompleteRFIDProcessingResponse>.ErrorData
                    {
                        Message = $"Failed to clear data before reprocessing: {clearResult.Message}"
                    };
                    return StatusCode((int)HttpStatusCode.InternalServerError, response);
                }
            }

            var result = await _service.ProcessCompleteWorkflowAsync(eventId, raceId);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<CompleteRFIDProcessingResponse>.ErrorData
                {
                    Message = _service.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Clears all processed RFID data (results, normalized readings, assignments) for a race.
        /// Optionally preserves raw uploads for reprocessing.
        /// WARNING: This cannot be undone. Use with caution.
        /// </summary>
        /// <param name="keepUploads">If true (default), preserves raw upload batches. If false, deletes everything.</param>
        [HttpDelete("{eventId}/{raceId}/clear-processed-data")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<ClearDataResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ClearProcessedData(
            string eventId,
            string raceId,
            [FromQuery] bool keepUploads = true)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Invalid input provided. Event ID and Race ID are required." });
            }

            var response = new ResponseBase<ClearDataResponse>();
            var result = await _service.ClearProcessedDataAsync(eventId, raceId, keepUploads);

            if (_service.HasError || result.Status == "Failed")
            {
                response.Error = new ResponseBase<ClearDataResponse>.ErrorData
                {
                    Message = _service.ErrorMessage ?? result.Message
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Assign checkpoints to readings for loop races where a single device is used at multiple checkpoints.
        /// Readings are assigned to checkpoints based on their time sequence per participant.
        /// </summary>
        [HttpPost("{eventId}/{raceId}/assign-checkpoints")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<AssignCheckpointsResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AssignCheckpoints(string eventId, string raceId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required" });
            }

            var response = new ResponseBase<AssignCheckpointsResponse>();
            var result = await _service.AssignCheckpointsForLoopRaceAsync(eventId, raceId);

            if (_service.HasError || result.Status == "Failed")
            {
                response.Error = new ResponseBase<AssignCheckpointsResponse>.ErrorData
                {
                    Message = _service.ErrorMessage ?? result.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        /// <summary>
        /// Create split times from normalized readings.
        /// Calculates cumulative time from race start and segment time from previous checkpoint.
        /// </summary>
        [HttpPost("{eventId}/{raceId}/create-split-times")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<CreateSplitTimesResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateSplitTimes(string eventId, string raceId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Event ID and Race ID are required" });
            }

            var response = new ResponseBase<CreateSplitTimesResponse>();
            var result = await _service.CreateSplitTimesFromNormalizedReadingsAsync(eventId, raceId);

            if (_service.HasError || result.Status == "Failed")
            {
                response.Error = new ResponseBase<CreateSplitTimesResponse>.ErrorData
                {
                    Message = _service.ErrorMessage ?? result.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
