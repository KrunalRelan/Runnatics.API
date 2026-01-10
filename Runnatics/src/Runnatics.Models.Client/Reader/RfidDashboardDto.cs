using Runnatics.Models.Client.FileUpload;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// DTO for RFID dashboard
    /// </summary>
    public class RfidDashboardDto
    {
        public int TotalReaders { get; set; }
        public int OnlineReaders { get; set; }
        public int OfflineReaders { get; set; }
        public long TotalReadsToday { get; set; }
        public int PendingUploads { get; set; }
        public int ProcessingUploads { get; set; }
        public int UnacknowledgedAlerts { get; set; }
        public List<ReaderAlertDto> RecentAlerts { get; set; } = new();
        public List<FileUploadStatusDto> RecentUploads { get; set; } = new();
    }
}
