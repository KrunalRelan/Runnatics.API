// ============================================================================
// File: Services/RaceReaderService.cs
// Purpose: Orchestrates reader lifecycle — register, prepare, start, stop.
//          Updated for int IDs, TenantId, and multi-checkpoint-per-device.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Hubs;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services;

public class RaceReaderService
{
    private readonly R700CommunicationService _r700;
    private readonly IUnitOfWork<RaceSyncDbContext> _repository;
    private readonly IHubContext<RaceHub> _raceHub;
    private readonly ILogger<RaceReaderService> _logger;

    public RaceReaderService(
        R700CommunicationService r700,
        IUnitOfWork<RaceSyncDbContext> repository,
        IHubContext<RaceHub> raceHub,
        ILogger<RaceReaderService> logger)
    {
        _r700 = r700;
        _repository = repository;
        _raceHub = raceHub;
        _logger = logger;
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

        var macRaw = (status.Reader.MacAddress ?? "")
            .Replace(":", "").Replace("-", "").ToLowerInvariant();

        var deviceRepo = _repository.GetRepository<Device>();

        var existing = await deviceRepo.GetQuery(d =>
                d.DeviceId == macRaw &&
                d.AuditProperties.IsActive &&
                !d.AuditProperties.IsDeleted)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Hostname = request.Hostname;
            existing.IpAddress = status.Reader.IpAddress;
            existing.FirmwareVersion = status.Reader.Firmware;
            existing.ReaderModel = status.Reader.Model;
            existing.IsOnline = true;
            existing.LastSeenAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(request.DeviceName))
                existing.Name = request.DeviceName;

            await deviceRepo.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return MapToResponse(existing);
        }

        var device = new Device
        {
            DeviceId = macRaw,
            Name = request.DeviceName,
            Hostname = request.Hostname,
            IpAddress = status.Reader.IpAddress,
            ReaderModel = status.Reader.Model ?? "Impinj R700",
            FirmwareVersion = status.Reader.Firmware,
            IsOnline = true,
            LastSeenAt = DateTime.UtcNow,
            TenantId = request.TenantId
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
        // webhook config and one preset. Group by DeviceId.
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
                var (isRunning, _) =
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
            MacAddress = device.DeviceId ?? "",
            IpAddress = device.IpAddress,
            FirmwareVersion = device.FirmwareVersion,
            ReaderModel = device.ReaderModel ?? "",
            IsOnline = device.IsOnline
        };
    }
}


    public async Task<RegisterDeviceResponse> RegisterDevice(
        RegisterDeviceRequest request)
    {
        _logger.LogInformation(
            "Registering device: {Hostname}", request.Hostname);

        // Talk to the reader to discover its identity
        var status = await _r700.DiscoverReader(request.Hostname);

        if (status?.Reader == null)
        {
            throw new InvalidOperationException(
                $"Cannot reach reader at '{request.Hostname}'. " +
                "Verify it is powered on and on the same network.");
        }

        // Normalize MAC: R700 may return "00:16:25:12:db:b0"
        // Your existing DeviceId column stores without colons: "00162512dbb0"
        var macRaw = (status.Reader.MacAddress ?? "")
            .Replace(":", "").Replace("-", "").ToLowerInvariant();

        // Check if device already exists by MAC
        var existing = await _deviceRepo.GetByDeviceId(macRaw);

        if (existing != null)
        {
            // Update existing — hostname/IP/firmware may have changed
            existing.Hostname = request.Hostname;
            existing.IpAddress = status.Reader.IpAddress;
            existing.FirmwareVersion = status.Reader.Firmware;
            existing.ReaderModel = status.Reader.Model;
            existing.IsOnline = true;
            existing.LastSeenAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(request.DeviceName))
                existing.Name = request.DeviceName;

            await _deviceRepo.Update(existing);
            return MapToResponse(existing);
        }

        // Create new device
        var device = new Device
        {
            DeviceId = macRaw,
            Name = request.DeviceName,
            Hostname = request.Hostname,
            IpAddress = status.Reader.IpAddress,
            ReaderModel = status.Reader.Model ?? "Impinj R700",
            FirmwareVersion = status.Reader.Firmware,
            IsOnline = true,
            LastSeenAt = DateTime.UtcNow,
            TenantId = request.TenantId
        };

        await _deviceRepo.Create(device);

        // Verify reader is in IoT Device Interface mode
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

        var assignments = await _deviceRepo
            .GetDeviceAssignmentsForRace(request.RaceId);

        if (!assignments.Any())
            throw new InvalidOperationException(
                "No devices assigned to this race.");

        var webhookUrl = $"{request.WebhookBaseUrl.TrimEnd('/')}/api/rfid/webhook";
        var preset = _r700.BuildMarathonPreset(request.RaceId);

        // Deduplicate: a device serving multiple checkpoints still only needs
        // ONE webhook config and ONE preset. Group by DeviceId.
        var distinctDevices = assignments
            .Where(a => a.Device != null)
            .GroupBy(a => a.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var assignment in distinctDevices)
        {
            var device = assignment.Device!;
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

                // Save preset ID on ALL assignments for this device
                foreach (var a in assignments.Where(a => a.DeviceId == device.Id))
                {
                    a.PresetId = preset.Id;
                    await _deviceRepo.UpdateAssignment(a);
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

        var assignments = await _deviceRepo
            .GetDeviceAssignmentsForRace(raceId);

        // Deduplicate: start each physical reader only once
        var distinctDevices = assignments
            .Where(a => a.Device != null && !string.IsNullOrEmpty(a.PresetId))
            .GroupBy(a => a.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var assignment in distinctDevices)
        {
            var device = assignment.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            if (string.IsNullOrEmpty(device.Hostname) ||
                string.IsNullOrEmpty(assignment.PresetId))
            {
                status.ErrorMessage = "Not configured. Run 'Prepare Race' first.";
                results.Add(status);
                continue;
            }

            try
            {
                var started = await _r700.StartPreset(
                    device.Hostname, assignment.PresetId);
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

        var assignments = await _deviceRepo
            .GetDeviceAssignmentsForRace(raceId);

        var distinctDevices = assignments
            .Where(a => a.Device?.Hostname != null)
            .GroupBy(a => a.DeviceId)
            .Select(g => g.First())
            .ToList();

        var results = new List<ReaderStatusDto>();

        foreach (var assignment in distinctDevices)
        {
            var device = assignment.Device!;
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
                _logger.LogError(ex,
                    "Failed to stop {Hostname}", device.Hostname);
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
        var assignments = await _deviceRepo
            .GetDeviceAssignmentsForRace(raceId);

        var distinctDevices = assignments
            .Where(a => a.Device?.Hostname != null)
            .GroupBy(a => a.DeviceId)
            .Select(g => g.First())
            .ToList();

        var tasks = distinctDevices.Select(async assignment =>
        {
            var device = assignment.Device!;
            var status = new ReaderStatusDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Hostname = device.Hostname ?? ""
            };

            try
            {
                var (isRunning, _) =
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

    private static RegisterDeviceResponse MapToResponse(Device device)
    {
        return new RegisterDeviceResponse
        {
            DeviceId = device.Id,
            Hostname = device.Hostname ?? "",
            MacAddress = device.DeviceId ?? "",
            IpAddress = device.IpAddress,
            FirmwareVersion = device.FirmwareVersion,
            ReaderModel = device.ReaderModel ?? "",
            IsOnline = device.IsOnline
        };
    }
}
