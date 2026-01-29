namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Checkpoint information for the race
    /// </summary>
    public class CheckpointInfoResponse
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal DistanceFromStart { get; set; }
        public bool IsMandatory { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }
}
