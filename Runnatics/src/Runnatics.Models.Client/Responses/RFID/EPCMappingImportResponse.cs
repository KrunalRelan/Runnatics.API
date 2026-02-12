namespace Runnatics.Models.Client.Responses.RFID
{
    public class EPCMappingImportResponse
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public int TotalRecords { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int NotFoundBibCount { get; set; }
        public string Status { get; set; } = "Processing";
        public List<string> NotFoundBibs { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
