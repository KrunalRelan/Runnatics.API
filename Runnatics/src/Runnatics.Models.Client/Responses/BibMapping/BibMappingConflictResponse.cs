namespace Runnatics.Models.Client.Responses.BibMapping
{
    public class BibMappingConflictResponse
    {
        public bool Success { get; set; } = false;

        public string ConflictType { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public ExistingBibMappingInfo? ExistingMapping { get; set; }

        public string? BibNumber { get; set; }

        public string? ExistingEpc { get; set; }
    }
}
