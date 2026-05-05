using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Export;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class ResultsExportService : IResultsExportService
    {
        private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly RaceSyncDbContext _context;
        private readonly IEncryptionService _encryptionService;

        public ResultsExportService(RaceSyncDbContext context, IEncryptionService encryptionService)
        {
            _context = context;
            _encryptionService = encryptionService;
        }

        public async Task<ExcelExportResult?> ExportResultsExcelAsync(
            GetLeaderboardRequest request,
            CancellationToken cancellationToken = default)
        {
            // 1. Decrypt IDs
            int eventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            int raceId  = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));

            // 2. Load event info
            var eventEntity = await _context.Events
                .Where(e => e.Id == eventId)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            // 3. Load race info
            var race = await _context.Races
                .Where(r => r.Id == raceId && r.EventId == eventId)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            // 4. Load leaderboard settings — race-level override first, then event-level
            var leaderboardSettings = await _context.LeaderboardSettings
                .Where(s => s.EventId == eventId &&
                            s.RaceId == raceId &&
                            s.OverrideSettings == true &&
                            s.AuditProperties.IsActive &&
                            !s.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (leaderboardSettings == null)
            {
                leaderboardSettings = await _context.LeaderboardSettings
                    .Where(s => s.EventId == eventId &&
                                s.RaceId == null &&
                                s.AuditProperties.IsActive &&
                                !s.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // 5. Load all results directly — no leaderboard restrictions
            var results = await _context.Results
                .Include(r => r.Participant)
                .Where(r => r.EventId == eventId &&
                            r.RaceId  == raceId &&
                            r.AuditProperties.IsActive &&
                            !r.AuditProperties.IsDeleted)
                .OrderBy(r => r.OverallRank ?? int.MaxValue)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (results.Count == 0)
                return null;

            // 6. Load split times
            var splits = await _context.SplitTimes
                .Include(s => s.ToCheckpoint)
                .Where(s => s.EventId == eventId &&
                            s.Participant.RaceId == raceId &&
                            s.AuditProperties.IsActive &&
                            !s.AuditProperties.IsDeleted)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Group splits by participantId for lookup
            var splitsByParticipant = splits
                .GroupBy(s => s.ParticipantId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 7. Determine columns from leaderboard settings
            bool showPace         = leaderboardSettings?.ShowPace          ?? false;
            bool showSplits       = leaderboardSettings?.ShowSplitTimes    ?? false;
            bool showGenderRank   = leaderboardSettings?.ShowGenderResults  ?? false;
            bool showCategoryRank = leaderboardSettings?.ShowCategoryResults?? false;
            bool showOverallRank  = leaderboardSettings?.ShowOverallResults ?? true;
            bool rankOnNet        = leaderboardSettings?.SortByOverallChipTime ?? true;

            // Distinct checkpoint names ordered by distance
            var checkpointColumns = showSplits
                ? splits
                    .Where(s => s.ToCheckpoint != null)
                    .GroupBy(s => s.ToCheckpointId)
                    .Select(g => g.First().ToCheckpoint)
                    .Where(c => !string.IsNullOrEmpty(c.Name))
                    .OrderBy(c => c.DistanceFromStart)
                    .ToList()
                : new List<Checkpoint>();

            var checkpointNames = checkpointColumns.Select(c => c.Name!).ToList();

            // 8. Build workbook
            using var workbook = new XLWorkbook();

            var headerBg = XLColor.FromHtml("#1565C0");
            var altRowBg = XLColor.FromHtml("#F5F5F5");

            // ── Sheet 1: Overall Results ──────────────────────────────────────────
            var ws1 = workbook.Worksheets.Add("Overall Results");

            var headers1 = new List<string> { "Overall Rank", "Bib", "Name", "Gender", "Category", "Status", "Gun Time", "Chip Time" };
            if (showPace)         headers1.Add("Average Pace");
            if (showSplits)       headers1.AddRange(checkpointNames);

            WriteHeaderRow(ws1, headers1, headerBg);
            ws1.SheetView.FreezeRows(1);

            for (int i = 0; i < results.Count; i++)
            {
                var r   = results[i];
                int row = i + 2;
                int col = 1;

                ws1.Cell(row, col++).Value = r.OverallRank?.ToString() ?? string.Empty;
                ws1.Cell(row, col++).Value = r.Participant?.BibNumber ?? string.Empty;
                ws1.Cell(row, col++).Value = r.Participant?.FullName  ?? string.Empty;
                ws1.Cell(row, col++).Value = r.Participant?.Gender    ?? string.Empty;
                ws1.Cell(row, col++).Value = r.Participant?.AgeCategory ?? string.Empty;
                ws1.Cell(row, col++).Value = r.Status ?? string.Empty;

                // Gun Time
                ws1.Cell(row, col++).Value = r.GunTime.HasValue
                    ? TimeSpan.FromMilliseconds(r.GunTime.Value).ToString(@"hh\:mm\:ss")
                    : string.Empty;

                // Chip Time (net, fall back to FinishTime)
                var chipMs = r.NetTime ?? r.FinishTime;
                ws1.Cell(row, col++).Value = chipMs.HasValue
                    ? TimeSpan.FromMilliseconds(chipMs.Value).ToString(@"hh\:mm\:ss")
                    : string.Empty;

                // Average Pace
                if (showPace)
                {
                    if (chipMs.HasValue && race?.Distance is { } dist && dist > 0)
                    {
                        var paceMinKm = (chipMs.Value / 60000.0m) / dist;
                        int mins = (int)paceMinKm;
                        int secs = (int)Math.Round((paceMinKm - mins) * 60);
                        ws1.Cell(row, col++).Value = $"{mins}:{secs:D2} min/km";
                    }
                    else
                    {
                        ws1.Cell(row, col++).Value = string.Empty;
                    }
                }

                // Splits
                if (showSplits && r.Participant != null)
                {
                    var participantSplits = splitsByParticipant.TryGetValue(r.ParticipantId, out var ps) ? ps : null;

                    var splitLookup = participantSplits?
                        .Where(s => s.ToCheckpoint?.Name != null)
                        .GroupBy(s => s.ToCheckpointId)
                        .ToDictionary(g => g.Key, g => g.Last())
                        ?? new Dictionary<int, SplitTimes>();

                    foreach (var cp in checkpointColumns)
                    {
                        if (splitLookup.TryGetValue(cp.Id, out var st) && st.SplitTimeMs.HasValue)
                            ws1.Cell(row, col++).Value = TimeSpan.FromMilliseconds(st.SplitTimeMs.Value).ToString(@"hh\:mm\:ss");
                        else
                            ws1.Cell(row, col++).Value = string.Empty;
                    }
                }

                if (i % 2 == 1)
                    ws1.Row(row).Style.Fill.BackgroundColor = altRowBg;
            }

            ws1.Columns().AdjustToContents();

            // ── Sheet 2: Category Results ─────────────────────────────────────────
            var ws2 = workbook.Worksheets.Add("Category Results");

            var catHeaders = new List<string> { "Category Rank", "Name", "Bib", "Chip Time" };
            WriteHeaderRow(ws2, catHeaders, headerBg);
            ws2.SheetView.FreezeRows(1);

            var catHeaderBg = XLColor.FromHtml("#BBDEFB");
            var genderOrder = new[] { "male", "female" };

            var finishers = results
                .Where(r => string.Equals(r.Status, "Finished", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var groupedByGenderCat = finishers
                .GroupBy(r => r.Participant?.Gender ?? "Unknown")
                .OrderBy(g =>
                {
                    var idx = Array.IndexOf(genderOrder, g.Key.ToLower());
                    return idx < 0 ? 999 : idx;
                })
                .SelectMany(genderGroup => genderGroup
                    .GroupBy(r => r.Participant?.AgeCategory ?? "Unknown")
                    .OrderBy(c => c.Key)
                    .Select(catGroup => new
                    {
                        Gender   = genderGroup.Key,
                        Category = catGroup.Key,
                        Entries  = rankOnNet
                            ? catGroup.OrderBy(r => r.NetTime ?? long.MaxValue).ToList()
                            : catGroup.OrderBy(r => r.GunTime ?? long.MaxValue).ToList()
                    }))
                .ToList();

            int currentRow2 = 2;
            int totalCatCols = catHeaders.Count;

            foreach (var group in groupedByGenderCat)
            {
                // Merged group header row
                var groupHeaderCell = ws2.Cell(currentRow2, 1);
                groupHeaderCell.Value = $"{group.Gender} - {group.Category}";
                groupHeaderCell.Style.Font.Bold = true;
                groupHeaderCell.Style.Fill.BackgroundColor = catHeaderBg;
                groupHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws2.Range(currentRow2, 1, currentRow2, totalCatCols).Merge();
                currentRow2++;

                for (int i = 0; i < group.Entries.Count; i++)
                {
                    var r   = group.Entries[i];
                    int col = 1;

                    ws2.Cell(currentRow2, col++).Value = i + 1; // category rank
                    ws2.Cell(currentRow2, col++).Value = r.Participant?.FullName ?? string.Empty;
                    ws2.Cell(currentRow2, col++).Value = r.Participant?.BibNumber ?? string.Empty;

                    var chipMs2 = r.NetTime ?? r.FinishTime;
                    ws2.Cell(currentRow2, col++).Value = chipMs2.HasValue
                        ? TimeSpan.FromMilliseconds(chipMs2.Value).ToString(@"hh\:mm\:ss")
                        : string.Empty;

                    if (i % 2 == 1)
                        ws2.Row(currentRow2).Style.Fill.BackgroundColor = altRowBg;

                    currentRow2++;
                }
            }

            ws2.Columns().AdjustToContents();

            // Save workbook
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = BuildFileName(eventEntity?.Name, race?.Title);

            return new ExcelExportResult
            {
                Content     = stream.ToArray(),
                ContentType = ExcelMimeType,
                FileName    = fileName,
            };
        }

        private static string BuildFileName(string? eventName, string? raceName)
        {
            static string Sanitize(string? value) =>
                string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : string.Concat(value.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');

            var parts = new[] { Sanitize(eventName), Sanitize(raceName), DateTime.UtcNow.ToString("yyyyMMdd"), "result" }
                .Where(p => !string.IsNullOrEmpty(p));

            return $"{string.Join("_", parts)}.xlsx";
        }

        private static void WriteHeaderRow(IXLWorksheet ws, List<string> headers, XLColor headerBg)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }
    }
}
