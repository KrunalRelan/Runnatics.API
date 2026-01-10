namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// DTO for reader status
    /// </summary>
    public class ReaderStatusDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? SerialNumber { get; set; }
        public string? IpAddress { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public decimal? CpuTemperatureCelsius { get; set; }
        public string? FirmwareVersion { get; set; }
        public long TotalReadsToday { get; set; }
        public DateTime? LastReadTimestamp { get; set; }
        public string? CheckpointName { get; set; }
        public List<AntennaStatusDto> Antennas { get; set; } = new();
        public int UnacknowledgedAlerts { get; set; }
    }
}
