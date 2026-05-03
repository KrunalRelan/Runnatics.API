using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Export;

namespace Runnatics.Services.Interface
{
    public interface IResultsExportService
    {
        /// <summary>
        /// Exports race results as an Excel workbook, honouring all leaderboard display settings.
        /// Returns null when no results are found for the given request.
        /// </summary>
        Task<ExcelExportResult?> ExportResultsExcelAsync(
            GetLeaderboardRequest request,
            CancellationToken cancellationToken = default);
    }
}
