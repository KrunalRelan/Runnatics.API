// ============================================================================
// File: Services/RaceReaderService.cs
// Purpose: Orchestrates reader lifecycle — register, prepare, start, stop.
//          Updated for int IDs, TenantId, and multi-checkpoint-per-device.
// ============================================================================

using AutoMapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Hubs;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services;

public class RaceReaderService
{
    private readonly R700CommunicationService _r700;
    private readonly IUnitOfWork<RaceSyncDbContext> _repository;
    private readonly IHubContext<RaceHub> _raceHub;
    private readonly ILogger<RaceReaderService> _logger;
    private readonly IMapper _mapper;

    public RaceReaderService(
        R700CommunicationService r700,
        IUnitOfWork<RaceSyncDbContext> repository,
        IHubContext<RaceHub> raceHub,
        ILogger<RaceReaderService> logger,
        IMapper mapper)
    {
        _r700 = r700;
        _repository = repository;
        _raceHub = raceHub;
        _logger = logger;
        _mapper = mapper;
    }

    // ── DEVICE REGISTRATION ──

    public async Task<RegisterDeviceResponse> RegisterDevice(
        RegisterDeviceRequest request)
    {
        _logger.LogInformation(
            "Registering device: {Hostname}", request.Hostname);

        var status = await _r700.DiscoverReader(request.Hostname);

        if (status?.Reader == null)
        {
            throw new InvalidOperationException(
                $"Cannot reach reader at '{request.Hostname}'. " +
                "Verify it is powered on and on the same network.");
        }

        // Normalize MAC: R700 may return "00:16:25:12:db:b0" → "00162512dbb0"
        var macRaw = (status.Reader.MacAddress ?? "")
            .Replace(":", "").Replace("-", "").ToLowerInvariant();

        var deviceRepo = _repository.GetRepository<Device>();

        var existing = await deviceRepo.GetQuery(d =>
                d.DeviceMacAddress == macRaw &&
                d.AuditProperties.IsActive &&
                !d.AuditProperties.IsDeleted)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            _mapper.Map(request, existing);
            _mapper.Map(status.Reader, existing);
            existing.IsOnline = true;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.AuditProperties.IsActive = true;
            existing.AuditProperties.IsDeleted = false;
            existing.AuditProperties.UpdatedDate = DateTime.UtcNow;

            await deviceRepo.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return MapToResponse(existing);
        }

        var device = _mapper.Map<Device>(request);
        _mapper.Map(status.Reader, device);
        device.DeviceMacAddress = macRaw;
        device.IsOnline = true;
        device.LastSeenAt = DateTime.UtcNow;
        device.AuditProperties = new AuditProperties
        {
            CreatedDate = DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false
        };

        await deviceRepo.AddAsync(device);
        await _repository.SaveChangesAsync();

        var rfidInterface = await _r700.GetRfidInterface(request.Hostname);
        if (rfidInterface != null &&
            !rfidInterface.Contains("rest", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Reader {Hostname} is in LLRP mode. Switching to IoT interface.",
                request.Hostname);
            await _r700.SetIoTDeviceInterface(request.Hostname);
        }

        return MapToResponse(device);
    }

    // ── PREPARE RACE ──

    public async Task<List<ReaderStatusDto>> PrepareRace(PrepareRaceRequest request)
    {
        _logger.LogInformation("Preparing race {RaceId}", request.RaceId);

        var checkpoints = await GetCheckpointsForRace(request.RaceId);

        if (!checkpoints.Any())
            throw new InvalidOperationException(
                "No devices assigned to this race.");

        var webhookUrl = $"{request.WebhookBaseUrl.TrimEnd('/')}/api/rfid/webhook";
        var preset = _r700.BuildMarathonPreset(request.RaceId);

        // Deduplicate: a device serving multiple checkpoints needs only one
        // webhook config and preset. Group by DeviceId.
        var distinctDevices = checkpoints
            .Where(cp => cp.Device != null)
            .GroupBy(cp => cp.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var checkpoint in distinctDevices)
        {
            var device = checkpoint.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            if (string.IsNullOrEmpty(device.Hostname))
            {
                status.ErrorMessage = "No hostname configured (offline-only device)";
                results.Add(status);
                continue;
            }

            try
            {
                var reachable = await _r700.IsReaderReachable(device.Hostname);
                status.IsReachable = reachable;

                if (!reachable)
                {
                    status.ErrorMessage = "Reader not reachable on the network";
                    results.Add(status);
                    continue;
                }

                var webhookOk = await _r700.ConfigureWebhook(
                    device.Hostname, webhookUrl);
                if (!webhookOk)
                {
                    status.ErrorMessage = "Failed to configure webhook";
                    results.Add(status);
                    continue;
                }

                var presetOk = await _r700.CreateInventoryPreset(
                    device.Hostname, preset);
                if (!presetOk)
                {
                    status.ErrorMessage = "Failed to create inventory preset";
                    results.Add(status);
                    continue;
                }

                status.IsReachable = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error preparing reader {Hostname}", device.Hostname);
                status.ErrorMessage = ex.Message;
            }

            results.Add(status);
        }

        await _raceHub.Clients.Group($"race-{request.RaceId}")
            .SendAsync("RacePrepared", results);

        return results;
    }

    // ── START RACE ──

    public async Task<List<ReaderStatusDto>> StartRace(int raceId)
    {
        _logger.LogInformation("Starting race {RaceId}", raceId);

        var checkpoints = await GetCheckpointsForRace(raceId);

        // Preset ID is deterministic — same as what PrepareRace configured
        var presetId = $"marathon-{raceId}";

        var distinctDevices = checkpoints
            .Where(cp => cp.Device != null && !string.IsNullOrEmpty(cp.Device.Hostname))
            .GroupBy(cp => cp.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var checkpoint in distinctDevices)
        {
            var device = checkpoint.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            try
            {
                var started = await _r700.StartPreset(device.Hostname!, presetId);
                status.IsReachable = true;
                status.IsRunning = started;
                if (!started) status.ErrorMessage = "Failed to start preset";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to start reader {Hostname}", device.Hostname);
                status.ErrorMessage = ex.Message;
            }

            results.Add(status);
        }

        await _raceHub.Clients.Group($"race-{raceId}")
            .SendAsync("RaceStarted", results);

        return results;
    }

    // ── STOP RACE ──

    public async Task<List<ReaderStatusDto>> StopRace(int raceId)
    {
        _logger.LogInformation("Stopping race {RaceId}", raceId);

        var checkpoints = await GetCheckpointsForRace(raceId);

        var distinctDevices = checkpoints
            .Where(cp => cp.Device?.Hostname != null)
            .GroupBy(cp => cp.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var checkpoint in distinctDevices)
        {
            var device = checkpoint.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            try
            {
                await _r700.StopReader(device.Hostname!);
                status.IsReachable = true;
                status.IsRunning = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop {Hostname}", device.Hostname);
                status.ErrorMessage = ex.Message;
            }

            results.Add(status);
        }

        await _raceHub.Clients.Group($"race-{raceId}")
            .SendAsync("RaceStopped", results);

        return results;
    }

    // ── STATUS ──

    public async Task<List<ReaderStatusDto>> GetRaceReaderStatuses(int raceId)
    {
        var checkpoints = await GetCheckpointsForRace(raceId);

        var distinctDevices = checkpoints
            .Where(cp => cp.Device?.Hostname != null)
            .GroupBy(cp => cp.DeviceId)
            .Select(g => g.First())
            .ToList();

        var tasks = distinctDevices.Select(async checkpoint =>
        {
            var device = checkpoint.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            try
            {
                (bool isRunning, string? _) =
                    await _r700.GetReaderRunStatus(device.Hostname!);
                status.IsReachable = true;
                status.IsRunning = isRunning;
            }
            catch
            {
                status.IsReachable = false;
            }

            return status;
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    // ── HELPERS ──

    private Task<List<Checkpoint>> GetCheckpointsForRace(int raceId)
        => _repository.GetRepository<Checkpoint>()
            .GetQuery(
                cp => cp.RaceId == raceId &&
                      cp.AuditProperties.IsActive &&
                      !cp.AuditProperties.IsDeleted,
                includeNavigationProperties: true)
            .Include(cp => cp.Device)
            .AsNoTracking()
            .ToListAsync();

    private static RegisterDeviceResponse MapToResponse(Device device)
    {
        return new RegisterDeviceResponse
        {
            DeviceId = device.Id,
            Hostname = device.Hostname ?? "",
            MacAddress = device.DeviceMacAddress ?? "",
            IpAddress = device.IpAddress,
            FirmwareVersion = device.FirmwareVersion,
            ReaderModel = device.ReaderModel ?? "",
            IsOnline = device.IsOnline
        };
    }
}
