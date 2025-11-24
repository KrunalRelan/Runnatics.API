namespace Runnatics.Models.Client.Responses.Participants
{
    public class ValidationError
    {
        public int RowNumber { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
        public string Value { get; set; }
    }
}