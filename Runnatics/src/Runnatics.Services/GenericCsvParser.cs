using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;
using System.Globalization;

namespace Runnatics.Services
{
    /// <summary>
    /// Generic CSV parser with configurable columns using CsvHelper
    /// </summary>
    public class GenericCsvParser : IFileParser
    {
        private readonly ILogger<GenericCsvParser> _logger;
        public FileFormat Format => FileFormat.GenericCsv;

        public GenericCsvParser(ILogger<GenericCsvParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var results = new List<ImpinjTagRead>();

            using var reader = new StreamReader(stream);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = mapping?.HasHeaderRow ?? true,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null
            };

            if (!string.IsNullOrEmpty(mapping?.Delimiter))
            {
                config.Delimiter = mapping.Delimiter;
            }

            using var csv = new CsvReader(reader, config);

            // Read header
            await csv.ReadAsync();
            csv.ReadHeader();

            // Map column names - use mapping or defaults
            var epcCol = mapping?.EpcColumn ?? "epc";
            var timestampCol = mapping?.TimestampColumn ?? "timestamp";
            var antennaCol = mapping?.AntennaPortColumn ?? "antenna_port";
            var rssiCol = mapping?.RssiColumn ?? "rssi";
            var phaseCol = mapping?.PhaseAngleColumn ?? "phase_angle";
            var dopplerCol = mapping?.DopplerColumn ?? "doppler";
            var channelCol = mapping?.ChannelIndexColumn ?? "channel";
            var readerSerialCol = mapping?.ReaderSerialColumn ?? "reader_serial";
            var tagCountCol = mapping?.TagSeenCountColumn ?? "tag_count";

            var timestampFormat = mapping?.TimestampFormat ?? "yyyy-MM-ddTHH:mm:ss.fffZ";

            while (await csv.ReadAsync())
            {
                try
                {
                    var epc = csv.GetField(epcCol);
                    if (string.IsNullOrWhiteSpace(epc)) continue;

                    var timestampStr = csv.GetField(timestampCol);
                    if (!DateTime.TryParseExact(timestampStr, timestampFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
                    {
                        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out timestamp))
                        {
                            _logger.LogWarning("Could not parse timestamp: {Timestamp}", timestampStr);
                            continue;
                        }
                    }

                    var tagRead = new ImpinjTagRead
                    {
                        Epc = epc,
                        Timestamp = timestamp.ToUniversalTime()
                    };

                    // Parse optional fields
                    if (int.TryParse(csv.GetField(antennaCol), out var antenna))
                        tagRead.AntennaPort = antenna;

                    if (double.TryParse(csv.GetField(rssiCol), out var rssi))
                        tagRead.RssiDbm = rssi;

                    if (double.TryParse(csv.GetField(phaseCol), out var phase))
                        tagRead.PhaseAngleDegrees = phase;

                    if (double.TryParse(csv.GetField(dopplerCol), out var doppler))
                        tagRead.DopplerFrequencyHz = doppler;

                    if (int.TryParse(csv.GetField(channelCol), out var channel))
                        tagRead.ChannelIndex = channel;

                    tagRead.ReaderSerialNumber = csv.GetField(readerSerialCol);

                    if (int.TryParse(csv.GetField(tagCountCol), out var tagCount))
                        tagRead.TagSeenCount = tagCount;

                    results.Add(tagRead);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing CSV row");
                }
            }

            _logger.LogInformation("Parsed {Count} records from generic CSV using CsvHelper", results.Count);
            return results;
        }
    }
}
