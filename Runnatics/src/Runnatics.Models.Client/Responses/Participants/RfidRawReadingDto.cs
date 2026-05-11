namespace Runnatics.Models.Client.Responses.Participants
{
    public class RfidRawReadingDto
    {
        public string Id { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string? Checkpoint { get; set; }
        public decimal? CheckpointDistance { get; set; }
        public string Device { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string? GunTime { get; set; }
        public string? NetTime { get; set; }
        public string ChipId { get; set; } = string.Empty;
        public string ProcessResult { get; set; } = string.Empty;
        public bool IsManual { get; set; }
        public bool IsDuplicate { get; set; }
        public bool IsNormalized { get; set; }
    }
}
