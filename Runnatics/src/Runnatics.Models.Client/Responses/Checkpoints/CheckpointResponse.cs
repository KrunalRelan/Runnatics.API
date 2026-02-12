namespace Runnatics.Models.Client.Responses.Checkpoints
{
    public class CheckpointResponse
    {
        public string Id { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string RaceId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public decimal DistanceFromStart { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string? ParentDeviceId { get; set; }
        public bool IsMandatory { get; set; }
        public string? DeviceName { get; set; }
        public string? ParentDeviceName { get; set; }
    }
}