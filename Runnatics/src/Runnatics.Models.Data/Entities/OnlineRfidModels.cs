// ============================================================================
// File: Models/OnlineRfidModels.cs
// Purpose: DTOs for the online RFID integration.
//
// NOTE: We do NOT redefine Device, RawRFIDReading, UploadBatch, Checkpoint,
//       ReadingCheckpointAssignment, ReadNormalized, or SplitTimes here.
//       Those are YOUR EXISTING entities — we write directly to them.
//
//       This file only contains:
//       - R700 webhook payload DTOs (what the reader sends us)
//       - SignalR event DTOs (what we push to React)
//       - API request/response DTOs (for the device management & race control APIs)
// ============================================================================

namespace Runnatics.Models.Data.Entities;

// ─────────────────────────────────────────────────────────────────────────────
// R700 IoT INTERFACE — Reader REST API communication models
// ─────────────────────────────────────────────────────────────────────────────

public class R700StatusResponse
{
    public string? Status { get; set; }
    public string? ActivePreset { get; set; }
    public R700ReaderInfo? Reader { get; set; }
}

public class R700ReaderInfo
{
    public string? Hostname { get; set; }
    public string? MacAddress { get; set; }
    public string? IpAddress { get; set; }
    public string? Firmware { get; set; }
    public string? Model { get; set; }
}

public class R700WebhookConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class R700InventoryPreset
{
    public string Id { get; set; } = string.Empty;
    public List<R700AntennaConfig> AntennaConfigs { get; set; } = new();
    public R700EventConfig? EventConfig { get; set; }
    public string? InventorySearchMode { get; set; }
    public int? EstimatedTagPopulation { get; set; }
}

public class R700AntennaConfig
{
    public int AntennaPort { get; set; }
    public int TransmitPowerCdbm { get; set; } = 3000;
    public bool IsEnabled { get; set; } = true;
}

public class R700EventConfig
{
    public R700TagInventoryEventConfig? TagInventoryEvent { get; set; }
}

public class R700TagInventoryEventConfig
{
    public bool TagReportingPortName { get; set; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────
// WEBHOOK PAYLOAD — What the R700 sends to your API
// ─────────────────────────────────────────────────────────────────────────────

public class R700WebhookPayload
{
    public string? Hostname { get; set; }
    public string? MacAddress { get; set; }
    public List<R700TagInventoryEvent>? TagInventoryEvents { get; set; }
}

public class R700TagInventoryEvent
{
    public string? Epc { get; set; }
    public string? FirstSeenTimestamp { get; set; }
    public string? LastSeenTimestamp { get; set; }
    public int? AntennaPort { get; set; }
    public int? PeakRssiCdbm { get; set; }
    public int? Channel { get; set; }
    public int? TagSeenCount { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// SIGNALR DTOs — Pushed to React frontend for live display
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pushed to React when a runner crosses a checkpoint (from webhook, pre-pipeline).
/// This is a RAW event — not yet deduplicated or normalized.
/// Dashboard should show "latest per bib per checkpoint" to handle duplicates.
/// </summary>
public class CheckpointCrossingEvent
{
    public string ParticipantName { get; set; } = string.Empty;
    public string BibNumber { get; set; } = string.Empty;
    public string CheckpointName { get; set; } = string.Empty;
    public string Epc { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Rssi { get; set; }
    public int AntennaPort { get; set; }
    public int RaceId { get; set; }
    public int CheckpointId { get; set; }
}

public class ReaderStatusChangedEvent
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsRunning { get; set; }
    public DateTime Timestamp { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// API DTOs — Device management & race control
// ─────────────────────────────────────────────────────────────────────────────

public class RegisterDeviceRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int TenantId { get; set; }
}

public class RegisterDeviceResponse
{
    public int DeviceId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? FirmwareVersion { get; set; }
    public string ReaderModel { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}

public class PrepareRaceRequest
{
    public int RaceId { get; set; }
    public string WebhookBaseUrl { get; set; } = string.Empty;
    public int TenantId { get; set; }
}

public class ReaderStatusDto
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public bool IsReachable { get; set; }
    public bool IsRunning { get; set; }
    public string? ErrorMessage { get; set; }
}
