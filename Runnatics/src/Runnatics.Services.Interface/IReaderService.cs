using Runnatics.Models.Client.Reader;

namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Service interface for reader device operations
    /// </summary>
    public interface IReaderService
    {
        /// <summary>
        /// Get all active readers with their status
        /// </summary>
        Task<List<ReaderStatusDto>> GetAllReadersAsync();

        /// <summary>
        /// Get a specific reader by ID
        /// </summary>
        Task<ReaderStatusDto?> GetReaderByIdAsync(int id);

        /// <summary>
        /// Get reader alerts with optional filtering
        /// </summary>
        Task<List<ReaderAlertDto>> GetAlertsAsync(bool unacknowledgedOnly = true, int? readerId = null);

        /// <summary>
        /// Acknowledge a reader alert
        /// </summary>
        Task<bool> AcknowledgeAlertAsync(long alertId, int userId, string? resolutionNotes = null);

        /// <summary>
        /// Get RFID dashboard summary
        /// </summary>
        Task<RfidDashboardDto> GetDashboardAsync();
    }
}
