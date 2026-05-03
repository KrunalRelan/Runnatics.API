using ClosedXML.Excel;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Export;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class ResultsExportService : IResultsExportService
    {
        private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly IResultsService _resultsService;

        public ResultsExportService(IResultsService resultsService)
        {
            _resultsService = resultsService;
        }

        public async Task<ExcelExportResult?> ExportResultsExcelAsync(
            GetLeaderboardRequest request,
            CancellationToken cancellationToken = default)
        {
            var leaderboard = await _resultsService.GetLeaderboardAsync(request);

            if (leaderboard?.Results == null || leaderboard.Results.Count == 0)
                return null;

            var settings = leaderboard.DisplaySettings;

            var entries = settings.ShowDnf
                ? leaderboard.Results
                : leaderboard.Results
                    .Where(e => !string.Equals(e.Status, "DNF", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var checkpointNames = settings.ShowSplitTimes
                ? entries
                    .Where(e => e.Splits != null)
                    .SelectMany(e => e.Splits!.Select(s => s.CheckpointName))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList()
                : new List<string>();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Results");

            var headerBg = XLColor.FromHtml("#1565C0");
            var altRowBg = XLColor.FromHtml("#F5F5F5");

            var headers = BuildHeaders(settings, checkpointNames);

            WriteHeaderRow(ws, headers, headerBg);
            ws.SheetView.FreezeRows(1);

            WriteDataRows(ws, entries, settings, checkpointNames, altRowBg);

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = BuildFileName(leaderboard.EventName, leaderboard.RaceName);

            return new ExcelExportResult
            {
                Content = stream.ToArray(),
                ContentType = ExcelMimeType,
                FileName = fileName,
            };
        }

        private static string BuildFileName(string? eventName, string? raceName)
        {
            static string Sanitize(string? value) =>
                string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : string.Concat(value.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');

            var parts = new[] { Sanitize(eventName), Sanitize(raceName), DateTime.UtcNow.ToString("yyyyMMdd"),"result" }
                .Where(p => !string.IsNullOrEmpty(p));

            return $"{string.Join("_", parts)}.xlsx";
        }

        // ── private helpers ────────────────────────────────────────────────────

        private static List<string> BuildHeaders(
            Runnatics.Models.Client.Responses.Results.LeaderboardDisplaySettings settings,
            List<string> checkpointNames)
        {
            var headers = new List<string> { "Rank", "Bib", "Name", "Gender", "Category", "Status", "Gun Time", "Chip Time" };

            if (settings.ShowOverallResults)  headers.Add("Overall Rank");
            if (settings.ShowGenderResults)   headers.Add("Gender Rank");
            if (settings.ShowCategoryResults) headers.Add("Category Rank");
            if (settings.ShowPace)            headers.Add("Avg Pace");

            headers.AddRange(checkpointNames);

            return headers;
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

        private static void WriteDataRows(
            IXLWorksheet ws,
            List<Runnatics.Models.Client.Responses.Results.LeaderboardEntry> entries,
            Runnatics.Models.Client.Responses.Results.LeaderboardDisplaySettings settings,
            List<string> checkpointNames,
            XLColor altRowBg)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                int row = i + 2;
                int col = 1;

                ws.Cell(row, col++).Value = entry.Rank;
                ws.Cell(row, col++).Value = entry.Bib ?? string.Empty;
                ws.Cell(row, col++).Value = entry.FullName ?? string.Empty;
                ws.Cell(row, col++).Value = entry.Gender ?? string.Empty;
                ws.Cell(row, col++).Value = entry.Category ?? string.Empty;
                ws.Cell(row, col++).Value = entry.Status ?? string.Empty;
                ws.Cell(row, col++).Value = entry.GunTime ?? string.Empty;
                ws.Cell(row, col++).Value = entry.NetTime ?? string.Empty;

                if (settings.ShowOverallResults)  ws.Cell(row, col++).Value = entry.OverallRank?.ToString() ?? string.Empty;
                if (settings.ShowGenderResults)   ws.Cell(row, col++).Value = entry.GenderRank?.ToString() ?? string.Empty;
                if (settings.ShowCategoryResults) ws.Cell(row, col++).Value = entry.CategoryRank?.ToString() ?? string.Empty;
                if (settings.ShowPace)            ws.Cell(row, col++).Value = entry.AveragePaceFormatted ?? string.Empty;

                if (settings.ShowSplitTimes)
                {
                    var splitLookup = entry.Splits?
                        .Where(s => !string.IsNullOrEmpty(s.CheckpointName))
                        .ToDictionary(s => s.CheckpointName, s => s.SplitTime)
                        ?? new Dictionary<string, string>();

                    foreach (var cp in checkpointNames)
                        ws.Cell(row, col++).Value = splitLookup.TryGetValue(cp, out var t) ? t : string.Empty;
                }

                if (i % 2 == 1)
                    ws.Row(row).Style.Fill.BackgroundColor = altRowBg;
            }
        }
    }
}
