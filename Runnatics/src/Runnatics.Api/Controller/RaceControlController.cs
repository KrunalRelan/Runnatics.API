// ============================================================================
// File: Controllers/RaceControlController.cs
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Data.Entities;
using Runnatics.Services;

namespace Runnatics.Controllers;

[ApiController]
[Route("api/race-control")]
public class RaceControlController : ControllerBase
{
    private readonly RaceReaderService _raceReaderService;
    private readonly ILogger<RaceControlController> _logger;

    public RaceControlController(
        RaceReaderService raceReaderService,
        ILogger<RaceControlController> logger)
    {
        _raceReaderService = raceReaderService;
        _logger = logger;
    }

    /// <summary>
    /// Configures webhooks + presets on all readers. Readers are NOT scanning yet.
    /// </summary>
    [HttpPost("prepare")]
    public async Task<ActionResult<List<ReaderStatusDto>>> PrepareRace(
        [FromBody] PrepareRaceRequest request)
    {
        try
        {
            var results = await _raceReaderService.PrepareRace(request);
            return Ok(results);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Starts all readers — GO LIVE. Readers begin scanning for RFID chips.
    /// </summary>
    [HttpPost("{raceId:int}/start")]
    public async Task<ActionResult<List<ReaderStatusDto>>> StartRace(int raceId)
    {
        var results = await _raceReaderService.StartRace(raceId);
        return Ok(results);
    }

    /// <summary>Stops all readers. Data is preserved.</summary>
    [HttpPost("{raceId:int}/stop")]
    public async Task<ActionResult<List<ReaderStatusDto>>> StopRace(int raceId)
    {
        var results = await _raceReaderService.StopRace(raceId);
        return Ok(results);
    }

    /// <summary>Live reader status for the dashboard.</summary>
    [HttpGet("{raceId:int}/reader-status")]
    public async Task<ActionResult<List<ReaderStatusDto>>> GetReaderStatuses(
        int raceId)
    {
        var statuses = await _raceReaderService.GetRaceReaderStatuses(raceId);
        return Ok(statuses);
    }
}
