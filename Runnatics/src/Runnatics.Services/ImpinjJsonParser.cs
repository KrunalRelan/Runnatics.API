using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;
using System.Text.Json;

namespace Runnatics.Services
{
    public class ImpinjJsonParser : IFileParser
    {
        private readonly ILogger<ImpinjJsonParser> _logger;
        public FileFormat Format => FileFormat.JSON;

        public ImpinjJsonParser(ILogger<ImpinjJsonParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var results = new List<ImpinjTagRead>();

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            try
            {
                var doc = JsonDocument.Parse(content);

                JsonElement tagsArray;
                if (doc.RootElement.TryGetProperty("tag_reads", out tagsArray) ||
                    doc.RootElement.TryGetProperty("tagReads", out tagsArray) ||
                    doc.RootElement.TryGetProperty("reads", out tagsArray))
                {
                    foreach (var tag in tagsArray.EnumerateArray())
                    {
                        var tagRead = ParseJsonTag(tag);
                        if (tagRead != null) results.Add(tagRead);
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in doc.RootElement.EnumerateArray())
                    {
                        var tagRead = ParseJsonTag(tag);
                        if (tagRead != null) results.Add(tagRead);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JSON file");
                throw;
            }

            _logger.LogInformation("Parsed {Count} records from JSON", results.Count);
            return results;
        }

        private ImpinjTagRead? ParseJsonTag(JsonElement tag)
        {
            try
            {
                var epc = GetStringProperty(tag, "epc", "Epc", "EPC", "tag_id", "tagId");
                if (string.IsNullOrWhiteSpace(epc)) return null;

                var timestampStr = GetStringProperty(tag, "timestamp", "Timestamp", "time", "readTime", "read_time");
                DateTime timestamp;
                if (string.IsNullOrWhiteSpace(timestampStr) || !DateTime.TryParse(timestampStr, out timestamp))
                {
                    if (tag.TryGetProperty("timestamp", out var tsElement) && tsElement.TryGetInt64(out var unixMs))
                    {
                        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
                    }
                    else
                    {
                        return null;
                    }
                }

                return new ImpinjTagRead
                {
                    Epc = epc,
                    Timestamp = timestamp.ToUniversalTime(),
                    AntennaPort = GetIntProperty(tag, "antenna_port", "antennaPort", "antenna"),
                    RssiDbm = GetDoubleProperty(tag, "peak_rssi", "peakRssi", "rssi"),
                    PhaseAngleDegrees = GetDoubleProperty(tag, "phase_angle", "phaseAngle", "phase"),
                    DopplerFrequencyHz = GetDoubleProperty(tag, "doppler", "dopplerFrequency"),
                    ChannelIndex = GetIntProperty(tag, "channel_index", "channelIndex", "channel"),
                    ReaderSerialNumber = GetStringProperty(tag, "reader_serial", "readerSerial", "serialNumber"),
                    ReaderHostname = GetStringProperty(tag, "reader_name", "readerName", "hostname"),
                    TagSeenCount = GetIntProperty(tag, "tag_seen_count", "tagSeenCount", "readCount") ?? 1
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? GetStringProperty(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }
            return null;
        }

        private static int? GetIntProperty(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var value))
                {
                    return value;
                }
            }
            return null;
        }

        private static double? GetDoubleProperty(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop) && prop.TryGetDouble(out var value))
                {
                    return value;
                }
            }
            return null;
        }
    }
}
