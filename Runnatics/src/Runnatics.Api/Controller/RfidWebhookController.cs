// ============================================================================
// File: Controllers/RfidWebhookController.cs
// Purpose: Receives real-time tag events from R700 readers via webhook POST.
//          Delegates to OnlineTagIngestionService which writes into the SAME
//          RawRFIDReading + UploadBatch tables your offline flow uses.
// ============================================================================

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Services;

namespace Runnatics.Controllers;

[ApiController]
[Route("api/rfid")]
public class RfidWebhookController : ControllerBase
{
    private readonly OnlineTagIngestionService _ingestionService;
    private readonly ILogger<RfidWebhookController> _logger;

    public RfidWebhookController(
        OnlineTagIngestionService ingestionService,
        ILogger<RfidWebhookController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Receives tag events from R700 readers via webhook POST.
    ///
    /// The R700 sends HTTP POST requests to this endpoint whenever it detects tags.
    /// Events arrive in batches — a single POST may contain multiple tag reads.
    ///
    /// This endpoint must respond quickly (< 1 second) to avoid reader retries.
    /// The heavy processing (dedup, normalize, results) happens asynchronously
    /// via ProcessCompleteWorkflowAsync when triggered.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook()
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                Request.Body, cancellationToken: HttpContext.RequestAborted);

            var payload = _ingestionService.ParseWebhookJson(doc.RootElement);

            if (payload.TagInventoryEvents?.Any() == true)
            {
                _logger.LogDebug(
                    "Webhook: {Count} events from {Host}",
                    payload.TagInventoryEvents.Count, payload.Hostname);

                await _ingestionService.ProcessWebhookPayload(payload);
            }

            // Always return 200 quickly — reader expects fast response
            return Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in webhook");
            return BadRequest("Invalid JSON");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook processing error");
            // Still 200 to prevent reader retries — data loss is worse
            return Ok();
        }
    }

    /// <summary>
    /// Health check. R700 may GET this to verify webhook URL is reachable.
    /// </summary>
    [HttpGet("webhook")]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "ready", timestamp = DateTime.UtcNow });
    }
}
