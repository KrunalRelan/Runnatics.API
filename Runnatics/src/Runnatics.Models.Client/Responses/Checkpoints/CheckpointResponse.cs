namespace Runnatics.Models.Client.Responses.Checkpoints
{
    public class CheckpointResponse
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int RaceId { get; set; }
        public string Name { get; set; }
        public decimal DistanceFromStart { get; set; }
        public int DeviceId { get; set; }
        public int? ParentDeviceId { get; set; }
        public bool IsMandatory { get; set; }
    }
}