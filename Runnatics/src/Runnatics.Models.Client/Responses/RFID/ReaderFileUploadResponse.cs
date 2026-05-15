namespace Runnatics.Models.Client.Responses.RFID
{
    public class ReaderFileUploadResponse
    {
        public string BatchId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int UploadedTags { get; set; }
        public int TotalTags { get; set; }
    }
}
