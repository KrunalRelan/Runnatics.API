using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// DTO for reader alerts
    /// </summary>
    public class ReaderAlertDto
    {
        public long Id { get; set; }
        public int ReaderDeviceId { get; set; }
        public string ReaderName { get; set; } = string.Empty;
        public ReaderAlertType AlertType { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsAcknowledged { get; set; }
        public string? AcknowledgedByUserName { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
