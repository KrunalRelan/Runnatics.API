namespace Runnatics.Models.Client.Responses.Participants
{
    public class ProcessingError
    {
        public int StagingId { get; set; }
        public int RowNumber { get; set; }
        public string? Bib { get; set; }
        public string? Name { get; set; }
        public string? ErrorMessage { get; set; }
    }
}