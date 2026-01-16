using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Runnatics.Models.Client.FileUpload;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services;
using Runnatics.Services.Interface;
using System.Security.Claims;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for file upload operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FileUploadController : ControllerBase
    {
        private readonly IFileUploadService _uploadService;
        private readonly IFileProcessingService _processingService;
        private readonly IRaceNotificationService _notificationService;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(
            IFileUploadService uploadService,
            IFileProcessingService processingService,
            IRaceNotificationService notificationService,
            ILogger<FileUploadController> logger)
        {
            _uploadService = uploadService;
            _processingService = processingService;
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary>
        /// Upload RFID read file(s) for offline data import
        /// </summary>
        /// <param name="request">The file upload form request containing file and metadata</param>
        /// <returns>File upload response</returns>
        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB limit
        [ProducesResponseType(typeof(FileUploadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<FileUploadResponse>> UploadFile([FromForm] FileUploadFormRequest request)
        {
            try
            {
                if (request.File == null || request.File.Length == 0)
                {
                    return BadRequest(new { error = "No file uploaded" });
                }

                var userId = GetCurrentUserId();
                var result = await _uploadService.UploadFileAsync(request, userId);

                // Get the batch for notification
                var batch = await _uploadService.GetBatchByIdAsync(result.BatchId);
                if (batch != null)
                {
                    await _notificationService.NotifyFileUploadedAsync(batch, User.Identity?.Name ?? "Unknown");
                }

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument during file upload");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file");
                return StatusCode(500, new { error = "An error occurred while uploading the file" });
            }
        }

        /// <summary>
        /// Upload multiple files at once
        /// </summary>
        /// <param name="request">The multi-file upload form request containing files and metadata</param>
        /// <returns>List of file upload responses</returns>
        [HttpPost("upload-multiple")]
        [RequestSizeLimit(500_000_000)] // 500MB limit for multiple files
        [ProducesResponseType(typeof(List<FileUploadResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FileUploadResponse>>> UploadMultipleFiles(
            [FromForm] MultiFileUploadFormRequest request)
        {
            var results = new List<FileUploadResponse>();
            var userId = GetCurrentUserId();

            if (request.Files == null || request.Files.Count == 0)
            {
                return BadRequest(new { error = "No files uploaded" });
            }

            foreach (var file in request.Files)
            {
                try
                {
                    var fileRequest = new FileUploadFormRequest
                    {
                        File = file,
                        RaceId = request.RaceId,
                        EventId = request.EventId,
                        ReaderDeviceId = request.ReaderDeviceId,
                        CheckpointId = request.CheckpointId,
                        Description = request.Description
                    };

                    var result = await _uploadService.UploadFileAsync(fileRequest, userId);
                    results.Add(result);

                    // Notify for each upload
                    var batch = await _uploadService.GetBatchByIdAsync(result.BatchId);
                    if (batch != null)
                    {
                        await _notificationService.NotifyFileUploadedAsync(batch, User.Identity?.Name ?? "Unknown");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file {FileName}", file.FileName);
                    results.Add(new FileUploadResponse
                    {
                        FileName = file.FileName,
                        Status = FileProcessingStatus.Failed,
                        Message = ex.Message
                    });
                }
            }

            return Ok(results);
        }

        /// <summary>
        /// Get status of a specific batch
        /// </summary>
        /// <param name="batchId">Batch ID</param>
        /// <returns>Batch status DTO</returns>
        [HttpGet("batch/{batchId}")]
        [ProducesResponseType(typeof(FileUploadStatusDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FileUploadStatusDto>> GetBatchStatus(int batchId)
        {
            try
            {
                var status = await _uploadService.GetBatchStatusAsync(batchId);
                return Ok(status);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "Batch not found" });
            }
        }

        /// <summary>
        /// Get status by batch GUID
        /// </summary>
        /// <param name="batchGuid">Batch GUID</param>
        /// <returns>Batch status DTO</returns>
        [HttpGet("batch/guid/{batchGuid}")]
        [ProducesResponseType(typeof(FileUploadStatusDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FileUploadStatusDto>> GetBatchStatusByGuid(Guid batchGuid)
        {
            var status = await _uploadService.GetBatchStatusByGuidAsync(batchGuid);
            if (status == null)
            {
                return NotFound(new { error = "Batch not found" });
            }
            return Ok(status);
        }

        /// <summary>
        /// Get batches for a race
        /// </summary>
        /// <param name="raceId">Race ID (encrypted)</param>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <param name="pageSize">Page size (default 20)</param>
        /// <returns>Paginated batch list</returns>
        [HttpGet("race/{raceId}/batches")]
        [ProducesResponseType(typeof(FileUploadBatchListDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<FileUploadBatchListDto>> GetRaceBatches(
            string raceId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var batches = await _uploadService.GetBatchesAsync(raceId, pageNumber, pageSize);
            return Ok(batches);
        }

        /// <summary>
        /// Get records in a batch
        /// </summary>
        /// <param name="batchId">Batch ID</param>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <param name="pageSize">Page size (default 100)</param>
        /// <returns>List of batch records</returns>
        [HttpGet("batch/{batchId}/records")]
        [ProducesResponseType(typeof(List<FileUploadRecordDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FileUploadRecordDto>>> GetBatchRecords(
            int batchId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 100)
        {
            var records = await _uploadService.GetBatchRecordsAsync(batchId, pageNumber, pageSize);
            return Ok(records);
        }

        /// <summary>
        /// Cancel a batch
        /// </summary>
        /// <param name="batchId">Batch ID to cancel</param>
        /// <returns>Success status</returns>
        [HttpPost("batch/{batchId}/cancel")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> CancelBatch(int batchId)
        {
            var success = await _uploadService.CancelBatchAsync(batchId);
            if (!success)
            {
                return NotFound(new { error = "Batch not found or cannot be cancelled" });
            }

            return Ok(new { message = "Batch cancelled" });
        }

        /// <summary>
        /// Reprocess a batch
        /// </summary>
        /// <param name="batchId">Batch ID to reprocess</param>
        /// <param name="request">Reprocess options</param>
        /// <returns>Success status</returns>
        [HttpPost("batch/{batchId}/reprocess")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> ReprocessBatch(int batchId, [FromBody] ReprocessBatchRequest? request = null)
        {
            request ??= new ReprocessBatchRequest { BatchId = batchId };
            request.BatchId = batchId;

            var success = await _uploadService.ReprocessBatchAsync(request);
            if (!success)
            {
                return NotFound(new { error = "Batch not found" });
            }

            return Ok(new { message = "Batch queued for reprocessing" });
        }

        /// <summary>
        /// Delete a batch
        /// </summary>
        /// <param name="batchId">Batch ID to delete</param>
        /// <returns>Success status</returns>
        [HttpDelete("batch/{batchId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DeleteBatch(int batchId)
        {
            var success = await _uploadService.DeleteBatchAsync(batchId);
            if (!success)
            {
                return NotFound(new { error = "Batch not found" });
            }

            return Ok(new { message = "Batch deleted" });
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : 0;
        }
    }
}
