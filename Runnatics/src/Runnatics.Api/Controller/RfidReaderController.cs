using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Reader;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for receiving real-time RFID reads from R700 readers (online mode)
    /// Configure R700 webhook to POST to these endpoints
    /// </summary>
    [ApiController]
    [Route("api/rfid")]
    public class RfidReaderController : ControllerBase
    {
        private readonly IRfidReaderService _rfidService;
        private readonly ILogger<RfidReaderController> _logger;
        private readonly IConfiguration _configuration;

        public RfidReaderController(
            IRfidReaderService rfidService,
            ILogger<RfidReaderController> logger,
            IConfiguration configuration)
        {
            _rfidService = rfidService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Receive a single tag read from R700 reader
        /// Configure R700 webhook URL to: POST https://yourapi.com/api/rfid/read
        /// </summary>
        /// <remarks>
        /// This endpoint is called by the R700 reader for each tag read.
        /// Authentication is via API key header for simplicity (R700 can't do complex auth).
        /// </remarks>
        [HttpPost("read")]
        [AllowAnonymous] // R700 uses API key authentication
        public async Task<IActionResult> ReceiveTagRead([FromBody] TagReadRequest request)
        {
            // Validate API key
            if (!ValidateApiKey())
            {
                _logger.LogWarning("Invalid API key for tag read from {Serial}", request?.ReaderSerial);
                return Unauthorized(new { error = "Invalid API key" });
            }

            if (request == null || string.IsNullOrEmpty(request.Epc))
            {
                return BadRequest(new { error = "EPC is required" });
            }

            var result = await _rfidService.ProcessTagReadAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return StatusCode(500, result);
        }

        /// <summary>
        /// Receive a batch of tag reads from R700 reader
        /// More efficient when reader buffers multiple reads
        /// </summary>
        [HttpPost("reads/batch")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveTagReadBatch([FromBody] TagReadBatchRequest request)
        {
            if (!ValidateApiKey())
            {
                return Unauthorized(new { error = "Invalid API key" });
            }

            if (request == null || request.Reads == null || request.Reads.Count == 0)
            {
                return BadRequest(new { error = "No reads provided" });
            }

            if (string.IsNullOrEmpty(request.ReaderSerial))
            {
                return BadRequest(new { error = "ReaderSerial is required" });
            }

            var result = await _rfidService.ProcessTagReadBatchAsync(request);

            return Ok(result);
        }

        /// <summary>
        /// Receive heartbeat from R700 reader
        /// Used to monitor reader health and connectivity
        /// </summary>
        [HttpPost("heartbeat")]
        [AllowAnonymous]
        public async Task<IActionResult> Heartbeat([FromBody] ReaderHeartbeatRequest request)
        {
            if (!ValidateApiKey())
            {
                return Unauthorized(new { error = "Invalid API key" });
            }

            if (request == null || string.IsNullOrEmpty(request.ReaderSerial))
            {
                return BadRequest(new { error = "ReaderSerial is required" });
            }

            var result = await _rfidService.ProcessHeartbeatAsync(request);

            return Ok(result);
        }

        /// <summary>
        /// Register a new reader or update existing
        /// Called when reader first connects or configuration changes
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterReader([FromBody] ReaderRegistrationRequest request)
        {
            if (!ValidateApiKey())
            {
                return Unauthorized(new { error = "Invalid API key" });
            }

            if (request == null || string.IsNullOrEmpty(request.SerialNumber))
            {
                return BadRequest(new { error = "SerialNumber is required" });
            }

            var result = await _rfidService.RegisterReaderAsync(request);

            return Ok(result);
        }

        /// <summary>
        /// Simple endpoint for R700 to check connectivity
        /// Can be used as a health check endpoint
        /// </summary>
        [HttpGet("ping")]
        [AllowAnonymous]
        public IActionResult Ping()
        {
            return Ok(new
            {
                status = "ok",
                serverTime = DateTime.UtcNow,
                version = "1.0"
            });
        }

        /// <summary>
        /// Get reader configuration by serial number
        /// Reader can call this on startup to get its assigned checkpoint, race, etc.
        /// </summary>
        [HttpGet("config/{serialNumber}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetReaderConfig(string serialNumber)
        {
            if (!ValidateApiKey())
            {
                return Unauthorized(new { error = "Invalid API key" });
            }

            var reader = await _rfidService.GetReaderBySerialAsync(serialNumber);

            if (reader == null)
            {
                return NotFound(new { error = "Reader not found" });
            }

            // Return registration response which includes config
            var result = await _rfidService.RegisterReaderAsync(new ReaderRegistrationRequest
            {
                SerialNumber = serialNumber
            });

            return Ok(result.Config);
        }
        private bool ValidateApiKey()
        {
            var configuredKey = _configuration["RfidReader:ApiKey"];

            // If no API key configured, allow all (development mode)
            if (string.IsNullOrEmpty(configuredKey))
            {
                _logger.LogWarning("No RFID API key configured - allowing all requests");
                return true;
            }

            // Check header
            if (Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
            {
                return apiKey == configuredKey;
            }

            // Also check query string (fallback for some devices)
            if (Request.Query.TryGetValue("apiKey", out var queryApiKey))
            {
                return queryApiKey == configuredKey;
            }

            return false;
        }

    }
}

