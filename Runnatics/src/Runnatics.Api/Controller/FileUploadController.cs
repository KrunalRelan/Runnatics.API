using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Runnatics.Services.Interface;
using System.Security.Claims;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FileUploadController : ControllerBase
    {
        private readonly IFileUploadService _uploadService;
        private readonly IFileProcessingService _processingService;
        private readonly IHubContext<RaceHub> _hubContext;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(
            IFileUploadService uploadService,
            IFileProcessingService processingService,
            IHubContext<RaceHub> hubContext,
            ILogger<FileUploadController> logger)
        {
            _uploadService = uploadService;
            _processingService = processingService;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Upload RFID read file(s) for offline data import
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB limit
        public async Task<ActionResult<FileUploadResponse>> UploadFile(
            [FromForm] IFormFile file,
            [FromForm] int raceId,
            [FromForm] int? eventId = null,
            [FromForm] int? readerDeviceId = null,
            [FromForm] int? checkpointId = null,
            [FromForm] string? description = null,
            [FromForm] UploadFileFormat? fileFormat = null,
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

                // Notify clients via SignalR
                await _hubContext.Clients.Group($"race_{raceId}")
                    .SendAsync("FileUploaded", result);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
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
        [HttpPost("upload-multiple")]
        [RequestSizeLimit(500_000_000)] // 500MB limit for multiple files
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
                }
                catch (Exception ex)
                {
                    results.Add(new FileUploadResponse
                    {
                        FileName = file.FileName,
                        Status = FileProcessingStatus.Failed,
                        Message = ex.Message
                    });
                }
            }

            // Notify clients
            await _hubContext.Clients.Group($"race_{raceId}")
                .SendAsync("MultipleFilesUploaded", results);

            return Ok(results);
        }

        /// <summary>
        /// Get status of a specific batch
        /// </summary>
        [HttpGet("batch/{batchId}")]
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
        [HttpGet("batch/guid/{batchGuid}")]
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
        /// Get all batches for a race
        /// </summary>
        [HttpGet("race/{raceId}/batches")]
        public async Task<ActionResult<FileUploadBatchListDto>> GetRaceBatches(
            int raceId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var batches = await _uploadService.GetBatchesAsync(raceId, page, pageSize);
            return Ok(batches);
        }

        /// <summary>
        /// Get records in a batch
        /// </summary>
        [HttpGet("batch/{batchId}/records")]
        public async Task<ActionResult<List<FileUploadRecordDto>>> GetBatchRecords(
            int batchId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            var records = await _uploadService.GetBatchRecordsAsync(batchId, page, pageSize);
            return Ok(records);
        }

        /// <summary>
        /// Manually trigger processing of a batch
        /// </summary>
        [HttpPost("batch/{batchId}/process")]
        public async Task<ActionResult> ProcessBatch(int batchId)
        {
            try
            {
                // Start processing in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _processingService.ProcessBatchAsync(batchId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing batch {BatchId}", batchId);
                    }
                });

                return Accepted(new { message = "Processing started" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting batch processing");
                return StatusCode(500, new { error = "Failed to start processing" });
            }
        }

        /// <summary>
        /// Reprocess a batch
        /// </summary>
        [HttpPost("batch/{batchId}/reprocess")]
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
        /// Cancel a batch processing
        /// </summary>
        [HttpPost("batch/{batchId}/cancel")]
        public async Task<ActionResult> CancelBatch(int batchId)
        {
            var success = await _uploadService.CancelBatchAsync(batchId);
            if (!success)
            {
                return BadRequest(new { error = "Cannot cancel batch - not currently processing" });
            }

            return Ok(new { message = "Batch cancelled" });
        }

        /// <summary>
        /// Delete a batch
        /// </summary>
        [HttpDelete("batch/{batchId}")]
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

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReaderController : ControllerBase
    {
        private readonly RunnaticsDbContext _context;
        private readonly IHubContext<RaceHub> _hubContext;
        private readonly ILogger<ReaderController> _logger;

        public ReaderController(
            RunnaticsDbContext context,
            IHubContext<RaceHub> hubContext,
            ILogger<ReaderController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Get all readers with status
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<ReaderStatusDto>>> GetReaders()
        {
            var readers = await _context.ReaderDevices
                .Include(r => r.HealthStatus)
                .Include(r => r.Antennas)
                .Include(r => r.Alerts.Where(a => !a.IsAcknowledged))
                .Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .Select(r => new ReaderStatusDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    SerialNumber = r.SerialNumber,
                    IpAddress = r.IpAddress,
                    IsOnline = r.HealthStatus != null && r.HealthStatus.IsOnline,
                    LastHeartbeat = r.HealthStatus != null ? r.HealthStatus.LastHeartbeat : null,
                    CpuTemperatureCelsius = r.HealthStatus != null ? r.HealthStatus.CpuTemperatureCelsius : null,
                    FirmwareVersion = r.HealthStatus != null ? r.HealthStatus.FirmwareVersion : null,
                    TotalReadsToday = r.HealthStatus != null ? r.HealthStatus.TotalReadsToday : 0,
                    LastReadTimestamp = r.HealthStatus != null ? r.HealthStatus.LastReadTimestamp : null,
                    Antennas = r.Antennas.Select(a => new AntennaStatusDto
                    {
                        Id = a.Id,
                        Port = a.AntennaPort,
                        Name = a.AntennaName,
                        IsEnabled = a.IsEnabled,
                        TxPowerCdBm = a.TxPowerCdBm,
                        Position = a.Position.ToString()
                    }).ToList(),
                    UnacknowledgedAlerts = r.Alerts.Count(a => !a.IsAcknowledged)
                })
                .ToListAsync();

            return Ok(readers);
        }

        /// <summary>
        /// Get reader by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ReaderStatusDto>> GetReader(int id)
        {
            var reader = await _context.ReaderDevices
                .Include(r => r.HealthStatus)
                .Include(r => r.Antennas)
                .Include(r => r.Alerts.Where(a => !a.IsAcknowledged))
                .Include(r => r.Checkpoint)
                .Where(r => r.Id == id)
                .Select(r => new ReaderStatusDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    SerialNumber = r.SerialNumber,
                    IpAddress = r.IpAddress,
                    IsOnline = r.HealthStatus != null && r.HealthStatus.IsOnline,
                    LastHeartbeat = r.HealthStatus != null ? r.HealthStatus.LastHeartbeat : null,
                    CpuTemperatureCelsius = r.HealthStatus != null ? r.HealthStatus.CpuTemperatureCelsius : null,
                    FirmwareVersion = r.HealthStatus != null ? r.HealthStatus.FirmwareVersion : null,
                    TotalReadsToday = r.HealthStatus != null ? r.HealthStatus.TotalReadsToday : 0,
                    LastReadTimestamp = r.HealthStatus != null ? r.HealthStatus.LastReadTimestamp : null,
                    CheckpointName = r.Checkpoint != null ? r.Checkpoint.Name : null,
                    Antennas = r.Antennas.Select(a => new AntennaStatusDto
                    {
                        Id = a.Id,
                        Port = a.AntennaPort,
                        Name = a.AntennaName,
                        IsEnabled = a.IsEnabled,
                        TxPowerCdBm = a.TxPowerCdBm,
                        Position = a.Position.ToString()
                    }).ToList(),
                    UnacknowledgedAlerts = r.Alerts.Count(a => !a.IsAcknowledged)
                })
                .FirstOrDefaultAsync();

            if (reader == null)
            {
                return NotFound();
            }

            return Ok(reader);
        }

        /// <summary>
        /// Get reader alerts
        /// </summary>
        [HttpGet("alerts")]
        public async Task<ActionResult<List<ReaderAlertDto>>> GetAlerts(
            [FromQuery] bool unacknowledgedOnly = true,
            [FromQuery] int? readerId = null)
        {
            var query = _context.ReaderAlerts
                .Include(a => a.ReaderDevice)
                .Where(a => !a.AuditProperties.IsDeleted);

            if (unacknowledgedOnly)
            {
                query = query.Where(a => !a.IsAcknowledged);
            }

            if (readerId.HasValue)
            {
                query = query.Where(a => a.ReaderDeviceId == readerId.Value);
            }

            var alerts = await query
                .OrderByDescending(a => a.AuditProperties.CreatedDate)
                .Take(100)
                .Select(a => new ReaderAlertDto
                {
                    Id = a.Id,
                    ReaderDeviceId = a.ReaderDeviceId,
                    ReaderName = a.ReaderDevice.Name,
                    AlertType = a.AlertType,
                    Severity = a.Severity,
                    Message = a.Message,
                    IsAcknowledged = a.IsAcknowledged,
                    AcknowledgedByUserName = a.AcknowledgedByUser != null ? a.AcknowledgedByUser.UserName : null,
                    AcknowledgedAt = a.AcknowledgedAt,
                    CreatedAt = a.AuditProperties.CreatedDate
                })
                .ToListAsync();

            return Ok(alerts);
        }

        /// <summary>
        /// Acknowledge an alert
        /// </summary>
        [HttpPost("alerts/{alertId}/acknowledge")]
        public async Task<ActionResult> AcknowledgeAlert(long alertId, [FromBody] string? resolutionNotes = null)
        {
            var alert = await _context.ReaderAlerts.FindAsync(alertId);
            if (alert == null)
            {
                return NotFound();
            }

            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

            alert.IsAcknowledged = true;
            alert.AcknowledgedByUserId = userId;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.ResolutionNotes = resolutionNotes;
            alert.AuditProperties.UpdatedBy = userId;
            alert.AuditProperties.UpdatedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Get dashboard summary
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<ActionResult<RfidDashboardDto>> GetDashboard()
        {
            var now = DateTime.UtcNow;
            var todayStart = now.Date;

            var readers = await _context.ReaderDevices
                .Include(r => r.HealthStatus)
                .Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .ToListAsync();

            var pendingUploads = await _context.FileUploadBatches
                .CountAsync(b => b.ProcessingStatus == FileProcessingStatus.Pending &&
                                !b.AuditProperties.IsDeleted);

            var processingUploads = await _context.FileUploadBatches
                .CountAsync(b => b.ProcessingStatus == FileProcessingStatus.Processing &&
                                !b.AuditProperties.IsDeleted);

            var unacknowledgedAlerts = await _context.ReaderAlerts
                .CountAsync(a => !a.IsAcknowledged && !a.AuditProperties.IsDeleted);

            var recentAlerts = await _context.ReaderAlerts
                .Include(a => a.ReaderDevice)
                .Where(a => !a.AuditProperties.IsDeleted)
                .OrderByDescending(a => a.AuditProperties.CreatedDate)
                .Take(10)
                .Select(a => new ReaderAlertDto
                {
                    Id = a.Id,
                    ReaderDeviceId = a.ReaderDeviceId,
                    ReaderName = a.ReaderDevice.Name,
                    AlertType = a.AlertType,
                    Severity = a.Severity,
                    Message = a.Message,
                    IsAcknowledged = a.IsAcknowledged,
                    CreatedAt = a.AuditProperties.CreatedDate
                })
                .ToListAsync();

            var recentUploads = await _context.FileUploadBatches
                .Where(b => !b.AuditProperties.IsDeleted)
                .OrderByDescending(b => b.AuditProperties.CreatedDate)
                .Take(10)
                .Select(b => new FileUploadStatusDto
                {
                    BatchId = b.Id,
                    BatchGuid = b.BatchGuid,
                    OriginalFileName = b.OriginalFileName,
                    Status = b.ProcessingStatus,
                    TotalRecords = b.TotalRecords,
                    ProcessedRecords = b.ProcessedRecords,
                    MatchedRecords = b.MatchedRecords,
                    CreatedAt = b.AuditProperties.CreatedDate
                })
                .ToListAsync();

            return Ok(new RfidDashboardDto
            {
                TotalReaders = readers.Count,
                OnlineReaders = readers.Count(r => r.HealthStatus?.IsOnline == true),
                OfflineReaders = readers.Count(r => r.HealthStatus?.IsOnline != true),
                TotalReadsToday = readers.Sum(r => r.HealthStatus?.TotalReadsToday ?? 0),
                PendingUploads = pendingUploads,
                ProcessingUploads = processingUploads,
                UnacknowledgedAlerts = unacknowledgedAlerts,
                RecentAlerts = recentAlerts,
                RecentUploads = recentUploads
            });
        }
    }
}
