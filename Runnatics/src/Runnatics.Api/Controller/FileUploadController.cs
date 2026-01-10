using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.FileUpload;
using Runnatics.Models.Data.Enumerations;
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
        /// <param name="file">The file to upload</param>
        /// <param name="raceId">Race ID to associate the upload with</param>
        /// <param name="eventId">Optional event ID</param>
        /// <param name="readerDeviceId">Optional reader device ID</param>
        /// <param name="checkpointId">Optional checkpoint ID</param>
        /// <param name="description">Optional description</param>
        /// <param name="fileFormat">Optional file format override</param>
        /// <param name="mappingId">Optional mapping ID</param>
        /// <returns>File upload response</returns>
        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB limit
        [ProducesResponseType(typeof(FileUploadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<FileUploadResponse>> UploadFile(
            [FromForm] IFormFile file,
            [FromForm] int raceId,
            [FromForm] int? eventId = null,
            [FromForm] int? readerDeviceId = null,
            [FromForm] int? checkpointId = null,
            [FromForm] string? description = null,
            [FromForm] FileFormat? fileFormat = null,
            [FromForm] int? mappingId = null)
        {
            try
            {
                var userId = GetCurrentUserId();

                var request = new FileUploadRequest
                {
                    RaceId = raceId,
                    EventId = eventId,
                    ReaderDeviceId = readerDeviceId,
                    CheckpointId = checkpointId,
                    Description = description,
                    FileFormat = fileFormat,
                    MappingId = mappingId
                };

                var result = await _uploadService.UploadFileAsync(file, request, userId);

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
        /// <param name="files">List of files to upload</param>
        /// <param name="raceId">Race ID to associate uploads with</param>
        /// <param name="eventId">Optional event ID</param>
        /// <param name="readerDeviceId">Optional reader device ID</param>
        /// <param name="checkpointId">Optional checkpoint ID</param>
        /// <param name="description">Optional description</param>
        /// <returns>List of file upload responses</returns>
        [HttpPost("upload-multiple")]
        [RequestSizeLimit(500_000_000)] // 500MB limit for multiple files
        [ProducesResponseType(typeof(List<FileUploadResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FileUploadResponse>>> UploadMultipleFiles(
            [FromForm] List<IFormFile> files,
            [FromForm] int raceId,
            [FromForm] int? eventId = null,
            [FromForm] int? readerDeviceId = null,
            [FromForm] int? checkpointId = null,
            [FromForm] string? description = null)
        {
            var results = new List<FileUploadResponse>();
            var userId = GetCurrentUserId();

            foreach (var file in files)
            {
                try
                {
                    var request = new FileUploadRequest
                    {
                        RaceId = raceId,
                        EventId = eventId,
                        ReaderDeviceId = readerDeviceId,
                        CheckpointId = checkpointId,
                        Description = description
                    };

                    var result = await _uploadService.UploadFileAsync(file, request, userId);
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
        /// <param name="raceId">Race ID</param>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <param name="pageSize">Page size (default 20)</param>
        /// <returns>Paginated batch list</returns>
        [HttpGet("race/{raceId}/batches")]
        [ProducesResponseType(typeof(FileUploadBatchListDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<FileUploadBatchListDto>> GetRaceBatches(
            int raceId,
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
