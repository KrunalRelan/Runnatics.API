using Microsoft.AspNetCore.Http;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class RFIDImportRequest
    {
        public required IFormFile File { get; set; }
        public string? DeviceId { get; set; }
        public string? CheckpointId { get; set; }
        public string TimeZoneId { get; set; } = "UTC";
        public bool TreatAsUtc { get; set; } = false;
    }
}
