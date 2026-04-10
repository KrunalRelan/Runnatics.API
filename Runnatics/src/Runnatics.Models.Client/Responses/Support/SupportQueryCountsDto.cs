namespace Runnatics.Models.Client.Responses.Support
{
    public class SupportQueryCountsDto
    {
        public int Total { get; set; }
        public int NewQuery { get; set; }
        public int Wip { get; set; }
        public int Closed { get; set; }
        public int Pending { get; set; }
        public int NotYetStarted { get; set; }
        public int Rejected { get; set; }
        public int Duplicate { get; set; }
    }
}
