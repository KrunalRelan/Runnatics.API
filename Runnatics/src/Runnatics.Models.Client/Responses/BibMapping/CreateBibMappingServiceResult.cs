namespace Runnatics.Models.Client.Responses.BibMapping
{
    public class CreateBibMappingServiceResult
    {
        public bool Success { get; set; }

        public bool Overridden { get; set; }

        public string? SuccessMessage { get; set; }

        public BibMappingResponse? Mapping { get; set; }

        public BibMappingConflictResponse? Conflict { get; set; }
    }
}
