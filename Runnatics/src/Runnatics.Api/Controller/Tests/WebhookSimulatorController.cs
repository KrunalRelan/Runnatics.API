// ============================================================================
// File: Tests/OnlineRfidIntegrationTests.cs
//
// PURPOSE: Test the full online RFID flow without physical R700 hardware.
//
// THREE TESTING APPROACHES (use all three):
//
//   1. WebhookSimulatorController — A controller you add to your API (dev only)
//      that generates fake R700 webhook payloads and POSTs them to your own
//      webhook endpoint internally. No external tools needed.
//
//   2. OnlineTagIngestionServiceTests — Unit tests that mock the repository
//      layer and verify OnlineTagIngestionService produces the correct
//      RawRFIDReading and UploadBatch records.
//
//   3. PipelineIntegrationTest — End-to-end test that:
//      (a) Injects fake webhook readings via the simulator
//      (b) Runs ProcessCompleteWorkflowAsync on those readings
//      (c) Verifies ReadNormalized, SplitTimes, and Results are created
//
// SETUP:
//   These tests require your existing test database with seeded data:
//   - At least 1 Event, 1 Race (with StartTime set), 3+ Checkpoints
//   - At least 1 Device registered with DeviceId (MAC) matching the simulator
//   - Checkpoints assigned to devices
//   - Participants with ChipAssignments (EPC mappings)
//
// If you don't have a test database, use the Seed Data section below.
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services;
using Runnatics.Services.Interface;

namespace Runnatics.Tests;

// ════════════════════════════════════════════════════════════════════════════
// APPROACH 1: WebhookSimulatorController
//
// Add this controller to your API project. It generates realistic R700
// webhook payloads and sends them to your own /api/rfid/webhook endpoint.
//
// In Startup/Program.cs, only register in Development:
//   if (app.Environment.IsDevelopment())
//       app.MapControllers(); // already mapped, simulator is just another controller
//
// Usage from Swagger/Postman:
//   POST /api/test/simulate-race?raceId=10&runnerCount=5&checkpointCount=3
//   POST /api/test/simulate-single?deviceMac=00162512dbb0&epc=418000A95119
//   POST /api/test/simulate-burst?deviceMac=00162512dbb0&count=20
//   GET  /api/test/verify-ingestion?batchSourceType=online_webhook
// ════════════════════════════════════════════════════════════════════════════

#if DEBUG  // Only compiles in Debug builds

[ApiController]
[Route("api/test")]
public class WebhookSimulatorController : ControllerBase
{
    private readonly IUnitOfWork<RaceSyncDbContext> _repository;
    private readonly IUserContextService _userContext;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<WebhookSimulatorController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookSimulatorController(
        IUnitOfWork<RaceSyncDbContext> repository,
        IUserContextService userContext,
        IEncryptionService encryptionService,
        ILogger<WebhookSimulatorController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _repository = repository;
        _userContext = userContext;
        _encryptionService = encryptionService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Simulates a complete race with realistic timing.
    /// Sends webhook POSTs to your own /api/rfid/webhook endpoint.
    ///
    /// Prerequisites in your database:
    ///   - Race with StartTime set
    ///   - Devices registered with DeviceId (MAC)
    ///   - Checkpoints assigned to devices
    ///   - Participants with ChipAssignments
    /// </summary>
    [HttpPost("simulate-race")]
    public async Task<IActionResult> SimulateRace(
        [FromQuery] int eventId,
        [FromQuery] int raceId,
        [FromQuery] int delayBetweenCheckpointsMs = 500)
    {
        _logger.LogInformation("═══ STARTING RACE SIMULATION for Race {RaceId} ═══", raceId);

        // Load race data
        var raceRepo = _repository.GetRepository<Race>();
        var race = await raceRepo.GetQuery(r => r.Id == raceId)
            .AsNoTracking().FirstOrDefaultAsync();

        if (race == null)
            return BadRequest(new { error = $"Race {raceId} not found" });

        if (!race.StartTime.HasValue)
            return BadRequest(new { error = "Race.StartTime not set. Set it before simulating." });

        // Load checkpoints ordered by distance
        var checkpointRepo = _repository.GetRepository<Checkpoint>();
        var checkpoints = await checkpointRepo.GetQuery(cp =>
                cp.RaceId == raceId &&
                cp.EventId == eventId &&
                cp.AuditProperties.IsActive &&
                !cp.AuditProperties.IsDeleted)
            .OrderBy(cp => cp.DistanceFromStart)
            .AsNoTracking()
            .ToListAsync();

        if (!checkpoints.Any())
            return BadRequest(new { error = "No checkpoints found for this race" });

        // Load devices
        var deviceRepo = _repository.GetRepository<Device>();
        var devices = await deviceRepo.GetQuery(d =>
                d.AuditProperties.IsActive && !d.AuditProperties.IsDeleted)
            .AsNoTracking()
            .ToDictionaryAsync(d => d.Id);

        // Load participants with chip assignments
        var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
        var participants = await chipAssignmentRepo.GetQuery(ca =>
                ca.Participant.RaceId == raceId &&
                !ca.UnassignedAt.HasValue &&
                ca.AuditProperties.IsActive &&
                !ca.AuditProperties.IsDeleted)
            .Include(ca => ca.Chip)
            .Include(ca => ca.Participant)
            .AsNoTracking()
            .Select(ca => new
            {
                ca.Participant.Id,
                Name = (ca.Participant.FirstName ?? "") + " " + (ca.Participant.LastName ?? ""),
                Bib = ca.Participant.BibNumber ?? "?",
                EPC = ca.Chip.EPC
            })
            .ToListAsync();

        if (!participants.Any())
            return BadRequest(new { error = "No participants with chip assignments found" });

        _logger.LogInformation(
            "Simulation setup: {Checkpoints} checkpoints, {Participants} participants",
            checkpoints.Count, participants.Count);

        // Build the simulation timeline
        var raceStart = race.StartTime!.Value;
        var results = new List<object>();
        var rng = new Random();
        var httpClient = _httpClientFactory.CreateClient();

        // For each checkpoint, simulate all runners crossing at realistic times
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint.DeviceId <= 0 || !devices.ContainsKey(checkpoint.DeviceId))
            {
                _logger.LogWarning("Checkpoint {Id} has no device assigned, skipping", checkpoint.Id);
                continue;
            }

            var device = devices[checkpoint.DeviceId];
            var deviceMac = device.DeviceId ?? "";
            var hostname = device.Hostname ?? $"simulated-{device.Id}";

            // Format MAC with colons for the webhook payload (R700 sends colons)
            var macFormatted = deviceMac.Length == 12
                ? string.Join(":", Enumerable.Range(0, 6).Select(i => deviceMac.Substring(i * 2, 2)))
                : deviceMac;

            _logger.LogInformation(
                "Simulating checkpoint '{Name}' ({Distance}km) via device {Hostname}",
                checkpoint.Name, checkpoint.DistanceFromStart, hostname);

            // Each runner crosses this checkpoint at a simulated time
            var tagEvents = new List<object>();

            foreach (var participant in participants)
            {
                // Calculate approximate crossing time based on distance and random pace
                var paceMinPerKm = 4.5 + rng.NextDouble() * 2.5; // 4.5 to 7.0 min/km
                var crossingTimeOffset = TimeSpan.FromMinutes(
                    (double)checkpoint.DistanceFromStart * paceMinPerKm);
                var crossingTime = raceStart + crossingTimeOffset;

                // Generate 2-4 reads per crossing (realistic: multiple antennas)
                var readCount = rng.Next(2, 5);
                for (int i = 0; i < readCount; i++)
                {
                    var readTime = crossingTime.AddMilliseconds(i * rng.Next(100, 600));
                    tagEvents.Add(new
                    {
                        epc = participant.EPC,
                        firstSeenTimestamp = readTime.ToString("O"),
                        lastSeenTimestamp = readTime.AddMilliseconds(50).ToString("O"),
                        antennaPort = rng.Next(1, 5),
                        peakRssiCdbm = -(rng.Next(5500, 7500)), // -55 to -75 dBm
                        channel = rng.Next(1, 51),
                        tagSeenCount = 1
                    });
                }
            }

            // Build webhook payload — exactly what an R700 would send
            var payload = new
            {
                hostname = hostname,
                macAddress = macFormatted,
                tag_inventory_events = tagEvents
            };

            // POST to our own webhook endpoint
            var webhookUrl = $"{Request.Scheme}://{Request.Host}/api/rfid/webhook";

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(webhookUrl, content);

                results.Add(new
                {
                    checkpoint = checkpoint.Name,
                    distance = checkpoint.DistanceFromStart,
                    device = hostname,
                    eventsInPayload = tagEvents.Count,
                    httpStatus = (int)response.StatusCode,
                    success = response.IsSuccessStatusCode
                });

                _logger.LogInformation(
                    "  → Sent {Count} events for {Participants} runners → HTTP {Status}",
                    tagEvents.Count, participants.Count, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    checkpoint = checkpoint.Name,
                    error = ex.Message
                });
            }

            // Small delay between checkpoints to simulate real-world pacing
            if (delayBetweenCheckpointsMs > 0)
                await Task.Delay(delayBetweenCheckpointsMs);
        }

        _logger.LogInformation("═══ RACE SIMULATION COMPLETE ═══");

        return Ok(new
        {
            message = "Race simulation complete",
            raceId,
            checkpointsSimulated = results.Count,
            participantsSimulated = participants.Count,
            results
        });
    }

    /// <summary>
    /// Sends a single tag event from a specific device.
    /// Useful for testing one runner at one checkpoint.
    ///
    /// Example: POST /api/test/simulate-single?deviceMac=00162512dbb0&epc=418000A95119
    /// </summary>
    [HttpPost("simulate-single")]
    public async Task<IActionResult> SimulateSingleEvent(
        [FromQuery] string deviceMac,
        [FromQuery] string epc,
        [FromQuery] string? hostname = null)
    {
        var macFormatted = deviceMac.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => deviceMac.Substring(i * 2, 2)))
            : deviceMac;

        var rng = new Random();
        var now = DateTime.UtcNow;

        var payload = new
        {
            hostname = hostname ?? $"simulated-{deviceMac[^4..]}",
            macAddress = macFormatted,
            tag_inventory_events = new[]
            {
                new
                {
                    epc = epc.ToUpperInvariant(),
                    firstSeenTimestamp = now.ToString("O"),
                    lastSeenTimestamp = now.AddMilliseconds(50).ToString("O"),
                    antennaPort = rng.Next(1, 5),
                    peakRssiCdbm = -(rng.Next(5500, 7500)),
                    channel = rng.Next(1, 51),
                    tagSeenCount = 1
                }
            }
        };

        var webhookUrl = $"{Request.Scheme}://{Request.Host}/api/rfid/webhook";
        var httpClient = _httpClientFactory.CreateClient();
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(webhookUrl, content);

        return Ok(new
        {
            message = "Single event sent",
            webhookUrl,
            httpStatus = (int)response.StatusCode,
            payload
        });
    }

    /// <summary>
    /// Sends a burst of N tag events from one device (multiple runners).
    /// Tests high-throughput scenarios like a pack of runners hitting a checkpoint.
    ///
    /// Pulls actual EPCs from your ChipAssignment table so the events
    /// match real participants.
    /// </summary>
    [HttpPost("simulate-burst")]
    public async Task<IActionResult> SimulateBurst(
        [FromQuery] string deviceMac,
        [FromQuery] int raceId,
        [FromQuery] int count = 20,
        [FromQuery] string? hostname = null)
    {
        var macFormatted = deviceMac.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => deviceMac.Substring(i * 2, 2)))
            : deviceMac;

        // Get actual EPCs from the database
        var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
        var epcs = await chipAssignmentRepo.GetQuery(ca =>
                ca.Participant.RaceId == raceId &&
                !ca.UnassignedAt.HasValue &&
                ca.AuditProperties.IsActive &&
                !ca.AuditProperties.IsDeleted)
            .Include(ca => ca.Chip)
            .Select(ca => ca.Chip.EPC)
            .Take(count)
            .ToListAsync();

        if (!epcs.Any())
            return BadRequest(new { error = "No chip assignments found for this race" });

        var rng = new Random();
        var now = DateTime.UtcNow;

        var events = new List<object>();
        for (int i = 0; i < count; i++)
        {
            var readTime = now.AddMilliseconds(i * rng.Next(50, 300));
            events.Add(new
            {
                epc = epcs[i % epcs.Count],
                firstSeenTimestamp = readTime.ToString("O"),
                lastSeenTimestamp = readTime.AddMilliseconds(50).ToString("O"),
                antennaPort = rng.Next(1, 5),
                peakRssiCdbm = -(rng.Next(5500, 7500)),
                channel = rng.Next(1, 51),
                tagSeenCount = 1
            });
        }

        var payload = new
        {
            hostname = hostname ?? $"simulated-{deviceMac[^4..]}",
            macAddress = macFormatted,
            tag_inventory_events = events
        };

        var webhookUrl = $"{Request.Scheme}://{Request.Host}/api/rfid/webhook";
        var httpClient = _httpClientFactory.CreateClient();
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(webhookUrl, content);

        return Ok(new
        {
            message = $"Burst of {count} events sent",
            httpStatus = (int)response.StatusCode,
            uniqueEpcs = events.Select(e => ((dynamic)e).epc).Distinct().Count(),
            totalEvents = events.Count
        });
    }

    /// <summary>
    /// Verifies that webhook data was ingested correctly.
    /// Checks RawRFIDReading and UploadBatch tables for online_webhook records.
    /// </summary>
    [HttpGet("verify-ingestion")]
    public async Task<IActionResult> VerifyIngestion(
        [FromQuery] int? eventId = null,
        [FromQuery] int? lastMinutes = 60)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-lastMinutes!.Value);

        var batchRepo = _repository.GetRepository<UploadBatch>();
        var readingRepo = _repository.GetRepository<RawRFIDReading>();

        var batchQuery = batchRepo.GetQuery(b =>
            b.SourceType == "online_webhook" &&
            b.AuditProperties.CreatedDate >= cutoff &&
            b.AuditProperties.IsActive &&
            !b.AuditProperties.IsDeleted);

        if (eventId.HasValue)
            batchQuery = batchQuery.Where(b => b.EventId == eventId.Value);

        var batches = await batchQuery
            .AsNoTracking()
            .OrderByDescending(b => b.AuditProperties.CreatedDate)
            .Select(b => new
            {
                b.Id,
                b.EventId,
                b.DeviceId,
                b.SourceType,
                b.IsLiveSync,
                b.TotalReadings,
                b.UniqueEpcs,
                b.Status,
                Created = b.AuditProperties.CreatedDate
            })
            .ToListAsync();

        var batchIds = batches.Select(b => b.Id).ToList();

        List<object> readingStats = new();
        if (batchIds.Any())
        {
            var stats = await readingRepo.GetQuery(r =>
                    batchIds.Contains(r.BatchId) &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                .AsNoTracking()
                .GroupBy(r => r.BatchId)
                .Select(g => new
                {
                    BatchId = g.Key,
                    TotalReadings = g.Count(),
                    UniqueEpcs = g.Select(r => r.Epc).Distinct().Count(),
                    UniqueDevices = g.Select(r => r.DeviceId).Distinct().Count(),
                    EarliestRead = g.Min(r => r.ReadTimeUtc),
                    LatestRead = g.Max(r => r.ReadTimeUtc),
                    ProcessResults = g.GroupBy(r => r.ProcessResult)
                        .Select(pr => new { Status = pr.Key, Count = pr.Count() })
                })
                .ToListAsync();
            readingStats = stats.Cast<object>().ToList();
        }

        // Check if any readings have been processed through the pipeline
        var normalizedRepo = _repository.GetRepository<ReadNormalized>();
        var normalizedCount = 0;
        if (batchIds.Any())
        {
            var onlineReadingIds = await readingRepo.GetQuery(r =>
                    batchIds.Contains(r.BatchId))
                .Select(r => r.Id)
                .ToListAsync();

            if (onlineReadingIds.Any())
            {
                normalizedCount = await normalizedRepo.GetQuery(n =>
                        n.RawReadId.HasValue &&
                        onlineReadingIds.Contains(n.RawReadId.Value))
                    .CountAsync();
            }
        }

        return Ok(new
        {
            message = "Ingestion verification",
            timeWindow = $"Last {lastMinutes} minutes",
            onlineBatches = batches.Count,
            batches,
            readingStats,
            pipeline = new
            {
                normalizedFromOnlineReadings = normalizedCount,
                pipelineHasRun = normalizedCount > 0
            }
        });
    }

    /// <summary>
    /// Runs the full ProcessCompleteWorkflowAsync on online data.
    /// This is the same pipeline your offline flow uses.
    /// Call this after simulate-race to process the simulated readings.
    /// </summary>
    [HttpPost("process-pipeline")]
    public async Task<IActionResult> RunPipeline(
        [FromQuery] int eventId,
        [FromQuery] int raceId,
        [FromServices] IRFIDImportService rfidService)
    {
        _logger.LogInformation(
            "Running ProcessCompleteWorkflowAsync for simulated online data. " +
            "Event: {EventId}, Race: {RaceId}", eventId, raceId);

        var encryptedEventId = _encryptionService.Encrypt(eventId.ToString());
        var encryptedRaceId = _encryptionService.Encrypt(raceId.ToString());

        var result = await rfidService.ProcessCompleteWorkflowAsync(
            encryptedEventId, encryptedRaceId);

        return Ok(new
        {
            message = "Pipeline execution complete",
            result.Status,
            result.TotalBatchesProcessed,
            result.TotalRawReadingsProcessed,
            result.CheckpointsAssigned,
            result.TotalNormalizedReadings,
            result.DuplicatesRemoved,
            result.SplitTimesCreated,
            result.TotalFinishers,
            result.ResultsCreated,
            result.ResultsUpdated,
            timings = new
            {
                phase1Ms = result.Phase1ProcessingMs,
                phase15Ms = result.Phase15AssignmentMs,
                phase2Ms = result.Phase2DeduplicationMs,
                phase25Ms = result.Phase25SplitTimesMs,
                phase3Ms = result.Phase3CalculationMs,
                totalMs = result.TotalProcessingTimeMs
            },
            result.Errors,
            result.Warnings
        });
    }

    /// <summary>
    /// Complete end-to-end test: Simulate → Process → Verify.
    /// One button does everything.
    /// </summary>
    [HttpPost("full-test")]
    public async Task<IActionResult> FullEndToEndTest(
        [FromQuery] int eventId,
        [FromQuery] int raceId,
        [FromServices] IRFIDImportService rfidService)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Simulate
        _logger.LogInformation("═══ FULL E2E TEST: Step 1 — Simulate Race ═══");
        var simResult = await SimulateRace(eventId, raceId, 200) as OkObjectResult;

        // Step 2: Process
        _logger.LogInformation("═══ FULL E2E TEST: Step 2 — Run Pipeline ═══");
        var pipelineResult = await RunPipeline(eventId, raceId, rfidService) as OkObjectResult;

        // Step 3: Verify
        _logger.LogInformation("═══ FULL E2E TEST: Step 3 — Verify ═══");
        var verifyResult = await VerifyIngestion(eventId, 5) as OkObjectResult;

        stopwatch.Stop();

        return Ok(new
        {
            message = "Full end-to-end test complete",
            totalTimeMs = stopwatch.ElapsedMilliseconds,
            step1_simulate = simResult?.Value,
            step2_pipeline = pipelineResult?.Value,
            step3_verify = verifyResult?.Value
        });
    }
}

#endif
