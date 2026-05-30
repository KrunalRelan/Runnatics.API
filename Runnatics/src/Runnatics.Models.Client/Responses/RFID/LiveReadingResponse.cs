namespace Runnatics.Models.Client.Responses.RFID
{
    public class LiveReadingResponse
    {
        public int Accepted { get; set; }
        public int Skipped { get; set; }
        public string BatchId { get; set; } = string.Empty;
    }
}
