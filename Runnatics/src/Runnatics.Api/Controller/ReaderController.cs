using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Reader;
using Runnatics.Services.Interface;
using System.Security.Claims;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for reader device operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReaderController(
        IReaderService readerService,
        ILogger<ReaderController> logger) : ControllerBase
    {

        /// <summary>
        /// Get all readers with status
        /// </summary>
        /// <returns>List of reader status DTOs</returns>
        [HttpGet]
        [ProducesResponseType(typeof(List<ReaderStatusDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<ReaderStatusDto>>> GetReaders()
        {
            var readers = await readerService.GetAllReadersAsync();
            return Ok(readers);
        }

        /// <summary>
        /// Get reader by ID
        /// </summary>
        /// <param name="id">Reader ID</param>
        /// <returns>Reader status DTO</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ReaderStatusDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ReaderStatusDto>> GetReader(int id)
        {
            var reader = await readerService.GetReaderByIdAsync(id);
            if (reader == null)
            {
                return NotFound(new { error = "Reader not found" });
            }
            return Ok(reader);
        }

        /// <summary>
        /// Get reader alerts
        /// </summary>
        /// <param name="unacknowledgedOnly">Filter to unacknowledged alerts only</param>
        /// <param name="readerId">Optional reader ID filter</param>
        /// <returns>List of reader alert DTOs</returns>
        [HttpGet("alerts")]
        [ProducesResponseType(typeof(List<ReaderAlertDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<ReaderAlertDto>>> GetAlerts(
            [FromQuery] bool unacknowledgedOnly = true,
            [FromQuery] int? readerId = null)
        {
            var alerts = await readerService.GetAlertsAsync(unacknowledgedOnly, readerId);
            return Ok(alerts);
        }

        /// <summary>
        /// Acknowledge an alert
        /// </summary>
        /// <param name="alertId">Alert ID to acknowledge</param>
        /// <param name="resolutionNotes">Optional resolution notes</param>
        /// <returns>Success status</returns>
        [HttpPost("alerts/{alertId}/acknowledge")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> AcknowledgeAlert(long alertId, [FromBody] string? resolutionNotes = null)
        {
            var userId = GetCurrentUserId();
            var success = await readerService.AcknowledgeAlertAsync(alertId, userId, resolutionNotes);

            if (!success)
            {
                return NotFound(new { error = "Alert not found" });
            }

            return Ok(new { message = "Alert acknowledged" });
        }

        /// <summary>
        /// Get dashboard summary
        /// </summary>
        /// <returns>RFID dashboard DTO</returns>
        [HttpGet("dashboard")]
        [ProducesResponseType(typeof(RfidDashboardDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<RfidDashboardDto>> GetDashboard()
        {
            var dashboard = await readerService.GetDashboardAsync();
            return Ok(dashboard);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : 0;
        }
    }
}
