namespace Runnatics.Models.Client.Responses.Checkpoints
{
    public class CheckpointResponse
    {
        public string Id { get; set; }
        public string EventId { get; set; }
        public string RaceId { get; set; }
        public string? Name { get; set; }
        public decimal DistanceFromStart { get; set; }
        public string DeviceId { get; set; }
        public string? ParentDeviceId { get; set; }
        public bool IsMandatory { get; set; }
        public string? DeviceName { get; set; }
        public string? ParentDeviceName { get; set; }
    }
}