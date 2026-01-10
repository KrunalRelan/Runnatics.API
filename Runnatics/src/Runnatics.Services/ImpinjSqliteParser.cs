using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Parser for Impinj R700 offline SQLite database files (.db)
    /// The R700 stores reads in SQLite when network is unavailable
    /// </summary>
    public class ImpinjSqliteParser : IFileParser
    {
        private readonly ILogger<ImpinjSqliteParser> _logger;

        public FileFormat Format => FileFormat.ImpinjSqlite;

        public ImpinjSqliteParser(ILogger<ImpinjSqliteParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var results = new List<ImpinjTagRead>();

            // SQLite requires file access, so we need to save the stream to a temp file
            var tempFile = Path.GetTempFileName();
            try
            {
                // Copy stream to temp file
                await using (var fileStream = File.Create(tempFile))
                {
                    await stream.CopyToAsync(fileStream);
                }

                // Open SQLite connection
                await using var connection = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly");
                await connection.OpenAsync();

                // Query the tags table (R700 offline storage schema)
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT 
                        epc,
                        time,
                        antenna,
                        rssi,
                        channel
                    FROM tags
                    ORDER BY time ASC";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    try
                    {
                        var epc = reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(epc)) continue;

                        var unixTime = reader.GetInt64(1);
                        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;

                        // Check if it's milliseconds instead of seconds
                        if (unixTime > 10000000000) // If > year 2286 in seconds, it's probably milliseconds
                        {
                            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
                        }

                        var tagRead = new ImpinjTagRead
                        {
                            Epc = epc,
                            Timestamp = timestamp,
                            AntennaPort = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            RssiDbm = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                            ChannelIndex = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                            TagSeenCount = 1
                        };

                        results.Add(tagRead);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing SQLite row");
                    }
                }

                // Also try to extract reader info from logs table if available
                try
                {
                    command.CommandText = "SELECT status, time, type FROM logs LIMIT 10";
                    await using var logReader = await command.ExecuteReaderAsync();
                    while (await logReader.ReadAsync())
                    {
                        var status = logReader.GetString(0);
                        _logger.LogDebug("SQLite log entry: {Status}", status);
                    }
                }
                catch
                {
                    // Logs table may not exist in all versions
                }

                _logger.LogInformation("Parsed {Count} tag reads from SQLite database", results.Count);
            }
            finally
            {
                // Clean up temp file
                try { File.Delete(tempFile); } catch { /* Ignore cleanup errors */ }
            }

            return results;
        }
    }
}
