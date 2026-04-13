// ============================================================================
// File: Tests/TestDataSeeder.cs
//
// PURPOSE: Seeds the minimum data needed to test the online RFID flow.
//          Run this once in your dev environment before using the simulator.
//
// Creates:
//   - 1 Event ("Test Marathon 2026")
//   - 1 Race ("Half Marathon 21.1km") with StartTime set
//   - 3 Devices (simulated R700 readers with MAC addresses)
//   - 5 Checkpoints (Start, 5km, 10km Turnaround, 15km, Finish)
//     with Start/Finish sharing one device (loop course!)
//   - 10 Participants with Chips and ChipAssignments
//
// Usage:
//   POST /api/test/seed-test-data
//   (returns all created IDs for use with the simulator)
//
// After seeding:
//   POST /api/test/full-test?eventId={id}&raceId={id}
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Tests;

#if DEBUG

[ApiController]
[Route("api/test")]
public class TestDataSeederController : ControllerBase
{
    private readonly IUnitOfWork<RaceSyncDbContext> _repository;
    private readonly IUserContextService _userContext;
    private readonly ILogger<TestDataSeederController> _logger;

    public TestDataSeederController(
        IUnitOfWork<RaceSyncDbContext> repository,
        IUserContextService userContext,
        ILogger<TestDataSeederController> logger)
    {
        _repository = repository;
        _userContext = userContext;
        _logger = logger;
    }

    [HttpPost("seed-test-data")]
    public async Task<IActionResult> SeedTestData()
    {
        var userId = _userContext.UserId;
        var tenantId = _userContext.TenantId;
        var now = DateTime.UtcNow;

        var audit = new Models.Data.Common.AuditProperties
        {
            CreatedBy = userId,
            CreatedDate = now,
            IsActive = true,
            IsDeleted = false
        };

        Event? evt = null;
        Race? race = null;
        List<Device> devices = new();
        List<Checkpoint> checkpoints = new();
        object[]? participantResults = null;
        DateTime raceStartTime = default;
        (string, decimal, int, int?)[] checkpointData = Array.Empty<(string, decimal, int, int?)>();

        try
        {
        var runnerData = new[]
        {
            ("Arun",    "Sharma",   "001", "418000A95101"),
            ("Priya",   "Kaur",     "002", "418000A95102"),
            ("Vikram",  "Singh",    "003", "418000A95103"),
            ("Meera",   "Patel",    "004", "418000A95104"),
            ("Rajesh",  "Kumar",    "005", "418000A95105"),
            ("Anita",   "Devi",     "006", "418000A95106"),
            ("Sunil",   "Verma",    "007", "418000A95107"),
            ("Neha",    "Gupta",    "008", "418000A95108"),
            ("Harpreet","Dhillon",  "042", "418000A95119"),
            ("Deepak",  "Malhotra", "099", "418000A95199"),
        };

        await _repository.ExecuteInTransactionAsync(async () =>
        {
            // ── EVENT ──
            var eventRepo = _repository.GetRepository<Event>();
            evt = new Event
            {
                Name = "Test Marathon 2026",
                TenantId = tenantId,
                AuditProperties = audit
            };
            await eventRepo.AddAsync(evt);
            await _repository.SaveChangesAsync();

            // ── RACE ──
            var raceRepo = _repository.GetRepository<Race>();
            raceStartTime = DateTime.UtcNow.AddMinutes(-30);
            race = new Race
            {
                EventId = evt.Id,
                Description = "Half Marathon 21.1km",
                Distance = 21.1m,
                StartTime = raceStartTime,
                AuditProperties = audit
            };
            await raceRepo.AddAsync(race);
            await _repository.SaveChangesAsync();

            // ── DEVICES ──
            var deviceRepo = _repository.GetRepository<Device>();
            var deviceData = new[]
            {
                ("00162512dbb0", "Box 1 - Start/Finish", "impinj-12-db-b0"),
                ("0016251292ae", "Box 2 - 5km",          "impinj-13-5f-24"),
                ("0016251292a1", "Box 3 - 10km Turn",    "impinj-13-5f-25"),
            };

            foreach (var (mac, name, hostname) in deviceData)
            {
                var device = new Device
                {
                    DeviceMacAddress = mac,
                    Name = name,
                    Hostname = hostname,
                    IsOnline = false,
                    TenantId = tenantId,
                    AuditProperties = audit
                };
                await deviceRepo.AddAsync(device);
                devices.Add(device);
            }
            await _repository.SaveChangesAsync();

            // ── CHECKPOINTS ──
            var checkpointRepo = _repository.GetRepository<Checkpoint>();
            checkpointData = new[]
            {
                ("Start",           0.0m,  devices[0].Id, (int?)null),
                ("5km",             5.0m,  devices[1].Id, (int?)null),
                ("10km Turnaround", 10.0m, devices[2].Id, (int?)null),
                ("15km (Return)",   15.0m, devices[1].Id, (int?)null),
                ("Finish",          21.1m, devices[0].Id, (int?)null),
            };

            foreach (var (name, distance, deviceId, parentDeviceId) in checkpointData)
            {
                var cp = new Checkpoint
                {
                    EventId = evt.Id,
                    RaceId = race.Id,
                    Name = name,
                    DistanceFromStart = distance,
                    DeviceId = deviceId,
                    ParentDeviceId = parentDeviceId,
                    AuditProperties = audit
                };
                await checkpointRepo.AddAsync(cp);
                checkpoints.Add(cp);
            }
            await _repository.SaveChangesAsync();

            // ── PARTICIPANTS + CHIPS + CHIP ASSIGNMENTS ──
            var participantRepo = _repository.GetRepository<Participant>();
            var chipRepo = _repository.GetRepository<Chip>();
            var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();

            var results = new List<object>();
            foreach (var (first, last, bib, epc) in runnerData)
            {
                var participant = new Participant
                {
                    EventId = evt.Id,
                    RaceId = race.Id,
                    FirstName = first,
                    LastName = last,
                    BibNumber = bib,
                    AuditProperties = audit
                };
                await participantRepo.AddAsync(participant);
                await _repository.SaveChangesAsync();

                var chip = new Chip
                {
                    TenantId = tenantId,
                    EPC = epc,
                    Status = "Assigned",
                    AuditProperties = audit
                };
                await chipRepo.AddAsync(chip);
                await _repository.SaveChangesAsync();

                var assignment = new ChipAssignment
                {
                    EventId = evt.Id,
                    ParticipantId = participant.Id,
                    ChipId = chip.Id,
                    AssignedAt = now,
                    AssignedByUserId = userId,
                    AuditProperties = audit
                };
                await chipAssignmentRepo.AddAsync(assignment);

                results.Add(new
                {
                    participantId = participant.Id,
                    name = $"{first} {last}",
                    bib,
                    epc,
                    chipId = chip.Id
                });
            }
            participantResults = results.ToArray();
        });

        _logger.LogInformation(
            "Test data seeded: Event {EventId}, Race {RaceId}, " +
            "{DeviceCount} devices, {CheckpointCount} checkpoints, " +
            "{ParticipantCount} participants",
            evt!.Id, race!.Id, devices.Count, checkpoints.Count, runnerData.Length);

        return Ok(new
        {
            message = "Test data seeded successfully",
            eventId = evt.Id,
            raceId = race.Id,
            raceStartTime,
            devices = devices.Select(d => new
            {
                d.Id,
                d.DeviceMacAddress,
                d.Name,
                d.Hostname
            }),
            checkpoints = checkpoints.Select((cp, i) => new
            {
                cp.Id,
                cp.Name,
                distance = checkpointData[i].Item2,
                deviceId = checkpointData[i].Item3,
                isSharedDevice = checkpointData[i].Item3 == devices[0].Id
                                 || checkpointData[i].Item3 == devices[1].Id
            }),
            participants = participantResults,
            nextSteps = new[]
            {
                $"POST /api/test/simulate-race?eventId={evt.Id}&raceId={race.Id}",
                $"POST /api/test/process-pipeline?eventId={evt.Id}&raceId={race.Id}",
                $"GET  /api/test/verify-ingestion?eventId={evt.Id}",
                $"POST /api/test/full-test?eventId={evt.Id}&raceId={race.Id}"
            }
        });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed test data");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cleans up test data created by seed-test-data.
    /// Soft-deletes everything with "Test Marathon 2026" event name.
    /// </summary>
    [HttpDelete("cleanup-test-data")]
    public async Task<IActionResult> CleanupTestData()
    {
        var eventRepo = _repository.GetRepository<Event>();
        var testEvents = await eventRepo.GetQuery(e =>
                e.Name == "Test Marathon 2026" &&
                e.AuditProperties.IsActive)
            .ToListAsync();

        if (!testEvents.Any())
            return Ok(new { message = "No test data found to clean up" });

        foreach (var evt in testEvents)
        {
            evt.AuditProperties.IsDeleted = true;
            evt.AuditProperties.IsActive = false;
            await eventRepo.UpdateAsync(evt);
        }
        await _repository.SaveChangesAsync();

        return Ok(new
        {
            message = $"Soft-deleted {testEvents.Count} test event(s)",
            note = "Related races, checkpoints, participants, and readings are filtered by IsActive/IsDeleted in all queries"
        });
    }
}

#endif
