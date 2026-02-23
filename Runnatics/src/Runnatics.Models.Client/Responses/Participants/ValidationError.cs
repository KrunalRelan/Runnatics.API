namespace Runnatics.Models.Client.Responses.Participants
{
    public class ValidationError
    {
        public int RowNumber { get; set; }
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}