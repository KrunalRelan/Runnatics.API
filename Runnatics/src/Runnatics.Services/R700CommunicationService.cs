// ============================================================================
// File: Services/R700CommunicationService.cs
// Purpose: Handles ALL HTTP communication with Impinj R700 readers.
//          Your .NET 8 API → Reader (outbound calls).
// ============================================================================

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runnatics.Models.Client.Configuration;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services;

public class R700CommunicationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<R700CommunicationService> _logger;
    private readonly R700Settings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public R700CommunicationService(
        IHttpClientFactory httpClientFactory,
        IOptions<R700Settings> settings,
        ILogger<R700CommunicationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("ImpinjR700");
        _logger = logger;
        _settings = settings.Value;
    }

    // ── DISCOVERY ──

    public async Task<R700StatusResponse?> DiscoverReader(string hostname)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/status");
            _logger.LogInformation("Discovering reader at {Hostname}", hostname);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content
                .ReadFromJsonAsync<R700StatusResponse>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot reach reader at {Hostname}", hostname);
            return null;
        }
    }

    public async Task<bool> IsReaderReachable(string hostname)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                BuildUrl(hostname, "/api/v1/status"));
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<(bool IsRunning, string? ActivePreset)> GetReaderRunStatus(
        string hostname)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/status");
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var status = await response.Content
                .ReadFromJsonAsync<R700StatusResponse>(JsonOptions);

            var isRunning = string.Equals(
                status?.Status, "running", StringComparison.OrdinalIgnoreCase);

            return (isRunning, status?.ActivePreset);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get status from {Hostname}", hostname);
            return (false, null);
        }
    }

    // ── CONFIGURATION ──

    public async Task<bool> ConfigureWebhook(string hostname, string webhookUrl)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/webhook/events");
            _logger.LogInformation(
                "Configuring webhook on {Hostname} → {Url}", hostname, webhookUrl);

            var config = new R700WebhookConfig
            {
                ServerUrl = webhookUrl,
                Enabled = true
            };

            var response = await _httpClient.PutAsJsonAsync(url, config, JsonOptions);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure webhook on {Hostname}", hostname);
            return false;
        }
    }

    public async Task<bool> CreateInventoryPreset(
        string hostname, R700InventoryPreset preset)
    {
        try
        {
            var url = BuildUrl(hostname,
                "/api/v1/profiles/inventory/presets/store");

            var response = await _httpClient.PostAsJsonAsync(url, preset, JsonOptions);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Preset '{PresetId}' created on {Hostname}", preset.Id, hostname);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create preset on {Hostname}", hostname);
            return false;
        }
    }

    public R700InventoryPreset BuildMarathonPreset(int raceId)
    {
        return new R700InventoryPreset
        {
            Id = $"marathon-{raceId}",
            AntennaConfigs = new List<R700AntennaConfig>
            {
                new() { AntennaPort = 1, TransmitPowerCdbm = 3000, IsEnabled = true },
                new() { AntennaPort = 2, TransmitPowerCdbm = 3000, IsEnabled = true },
                new() { AntennaPort = 3, TransmitPowerCdbm = 3000, IsEnabled = true },
                new() { AntennaPort = 4, TransmitPowerCdbm = 3000, IsEnabled = true }
            },
            EventConfig = new R700EventConfig
            {
                TagInventoryEvent = new R700TagInventoryEventConfig
                {
                    TagReportingPortName = true
                }
            },
            InventorySearchMode = "dual-target",
            EstimatedTagPopulation = 50
        };
    }

    // ── CONTROL ──

    public async Task<bool> StartPreset(string hostname, string presetId)
    {
        try
        {
            var url = BuildUrl(hostname,
                $"/api/v1/profiles/inventory/presets/{presetId}/start");

            _logger.LogInformation(
                "Starting preset '{PresetId}' on {Hostname}", presetId, hostname);

            var response = await _httpClient.PostAsync(url, null);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start preset on {Hostname}", hostname);
            return false;
        }
    }

    public async Task<bool> StopReader(string hostname)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/profiles/stop");
            var response = await _httpClient.PostAsync(url, null);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Reader {Hostname} STOPPED", hostname);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop reader {Hostname}", hostname);
            return false;
        }
    }

    // ── SYSTEM ──

    public async Task<string?> GetRfidInterface(string hostname)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/system/rfid/interface");
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            return doc.RootElement.TryGetProperty("rfidInterface", out var val)
                ? val.GetString() : null;
        }
        catch { return null; }
    }

    public async Task<bool> SetIoTDeviceInterface(string hostname)
    {
        try
        {
            var url = BuildUrl(hostname, "/api/v1/system/rfid/interface");
            var content = new StringContent(
                JsonSerializer.Serialize(
                    new { rfidInterface = "rest" }, JsonOptions),
                Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(url, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Switched {Hostname} to IoT Device Interface", hostname);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to set IoT interface on {Hostname}", hostname);
            return false;
        }
    }

    private string BuildUrl(string hostname, string path)
    {
        var scheme = _settings.UseHttps ? "https" : "http";
        return $"{scheme}://{hostname}{path}";
    }
}
