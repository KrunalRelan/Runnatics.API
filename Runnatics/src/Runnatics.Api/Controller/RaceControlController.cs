// ============================================================================
// File: Controllers/DeviceManagementController.cs
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Data.Entities;
using Runnatics.Services;

namespace Runnatics.Controllers;

[ApiController]
[Route("api/devices")]
public class DeviceManagementController : ControllerBase
{
    private readonly RaceReaderService _raceReaderService;
    private readonly IDeviceRepository _deviceRepo;

    public DeviceManagementController(
        RaceReaderService raceReaderService,
        IDeviceRepository deviceRepo)
    {
        _raceReaderService = raceReaderService;
        _deviceRepo = deviceRepo;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterDeviceResponse>> Register(
        [FromBody] RegisterDeviceRequest request)
    {
        try
        {
            var result = await _raceReaderService.RegisterDevice(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<Device>>> GetAll(
        [FromQuery] int tenantId)
    {
        var devices = await _deviceRepo.GetAllByTenant(tenantId);
        return Ok(devices);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Device>> GetById(int id)
    {
        var device = await _deviceRepo.GetById(id);
        return device == null ? NotFound() : Ok(device);
    }

    [HttpPost("assign")]
    public async Task<IActionResult> AssignToCheckpoint(
        [FromBody] AssignDeviceToCheckpointRequest request)
    {
        var assignment = new RaceCheckpointDevice
        {
            RaceId = request.RaceId,
            CheckpointId = request.CheckpointId,
            DeviceId = request.DeviceId,
            Mode = request.Mode,
            SortOrder = request.SortOrder,
            TenantId = request.TenantId
        };

        await _deviceRepo.CreateAssignment(assignment);
        return Ok(assignment);
    }
}

// ============================================================================
// File: Controllers/RaceControlController.cs
// ============================================================================

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
