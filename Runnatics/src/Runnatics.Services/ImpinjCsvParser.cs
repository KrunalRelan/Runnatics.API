using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;
using System.Globalization;

namespace Runnatics.Services
{
    /// <summary>
    /// Parser for Impinj R700 CSV exports
    /// </summary>
    public class ImpinjCsvParser : IFileParser
    {
        private readonly ILogger<ImpinjCsvParser> _logger;
        public FileFormat Format => FileFormat.CSV;

        public ImpinjCsvParser(ILogger<ImpinjCsvParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var results = new List<ImpinjTagRead>();

            using var reader = new StreamReader(stream);

            var hasHeader = mapping?.HasHeaderRow ?? true;
            var delimiter = mapping?.Delimiter ?? ",";

            // Map column names
            var epcCol = mapping?.EpcColumn ?? "epc";
            var timestampCol = mapping?.TimestampColumn ?? "timestamp";
            var antennaCol = mapping?.AntennaPortColumn ?? "antenna_port";
            var rssiCol = mapping?.RssiColumn ?? "peak_rssi";
            var phaseCol = mapping?.PhaseAngleColumn ?? "phase_angle";
            var dopplerCol = mapping?.DopplerColumn ?? "doppler";
            var channelCol = mapping?.ChannelIndexColumn ?? "channel_index";
            var readerSerialCol = mapping?.ReaderSerialColumn ?? "reader_serial";
            var tagCountCol = mapping?.TagSeenCountColumn ?? "tag_seen_count";

            var timestampFormat = mapping?.TimestampFormat ?? "yyyy-MM-ddTHH:mm:ss.fffZ";

            Dictionary<string, int>? headerMap = null;
            int lineNumber = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = line.Split(delimiter[0]);

                // First line is header
                if (lineNumber == 1 && hasHeader)
                {
                    headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        headerMap[fields[i].Trim().Trim('"')] = i;
                    }
                    continue;
                }

                try
                {
                    var epc = GetFieldValue(fields, headerMap, epcCol, 0);
                    if (string.IsNullOrWhiteSpace(epc)) continue;

                    var timestampStr = GetFieldValue(fields, headerMap, timestampCol, 1);
                    if (!DateTime.TryParseExact(timestampStr, timestampFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
                    {
                        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp))
                        {
                            _logger.LogWarning("Could not parse timestamp at line {Line}: {Timestamp}", lineNumber, timestampStr);
                            continue;
                        }
                    }

                    var tagRead = new ImpinjTagRead
                    {
                        Epc = epc.Trim().Trim('"'),
                        Timestamp = timestamp
                    };

                    // Parse optional fields
                    var antennaStr = GetFieldValue(fields, headerMap, antennaCol, -1);
                    if (int.TryParse(antennaStr, out var antenna))
                        tagRead.AntennaPort = antenna;

                    var rssiStr = GetFieldValue(fields, headerMap, rssiCol, -1);
                    if (double.TryParse(rssiStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rssi))
                        tagRead.RssiDbm = rssi;

                    var phaseStr = GetFieldValue(fields, headerMap, phaseCol, -1);
                    if (double.TryParse(phaseStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var phase))
                        tagRead.PhaseAngleDegrees = phase;

                    var dopplerStr = GetFieldValue(fields, headerMap, dopplerCol, -1);
                    if (double.TryParse(dopplerStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var doppler))
                        tagRead.DopplerFrequencyHz = doppler;

                    var channelStr = GetFieldValue(fields, headerMap, channelCol, -1);
                    if (int.TryParse(channelStr, out var channel))
                        tagRead.ChannelIndex = channel;

                    tagRead.ReaderSerialNumber = GetFieldValue(fields, headerMap, readerSerialCol, -1)?.Trim().Trim('"');

                    var tagCountStr = GetFieldValue(fields, headerMap, tagCountCol, -1);
                    if (int.TryParse(tagCountStr, out var tagCount))
                        tagRead.TagSeenCount = tagCount;

                    results.Add(tagRead);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing line {Line}", lineNumber);
                }
            }

            _logger.LogInformation("Parsed {Count} records from CSV", results.Count);
            return results;
        }

        private static string? GetFieldValue(string[] fields, Dictionary<string, int>? headerMap, string columnName, int defaultIndex)
        {
            int index = defaultIndex;
            if (headerMap != null && headerMap.TryGetValue(columnName, out var mappedIndex))
            {
                index = mappedIndex;
            }

            if (index >= 0 && index < fields.Length)
            {
                return fields[index];
            }

            return null;
        }
    }
}
