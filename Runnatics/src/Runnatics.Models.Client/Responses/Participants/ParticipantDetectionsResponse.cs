namespace Runnatics.Models.Client.Responses.Participants
{
    public class ParticipantDetectionsResponse
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public decimal? ManualDistance { get; set; }
        public List<CheckpointDetectionGroupDto> Checkpoints { get; set; } = new();
    }

    public class CheckpointDetectionGroupDto
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string CheckpointName { get; set; } = string.Empty;
        public bool IsMandatory { get; set; }
        public List<DetectionRowDto> Detections { get; set; } = new();
    }

    public class DetectionRowDto
    {
        public string ReadingId { get; set; } = string.Empty;
        public DateTime ReadTimeUtc { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string ReaderName { get; set; } = string.Empty;
        public int? RssiDbm { get; set; }
        public string ProcessResult { get; set; } = string.Empty;
        public TimeSpan? ManualTime { get; set; }
        public bool IsManualEntry { get; set; }
        public string? Notes { get; set; }
    }
}
