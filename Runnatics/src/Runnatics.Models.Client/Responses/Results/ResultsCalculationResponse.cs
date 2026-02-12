namespace Runnatics.Models.Client.Responses.Results
{
    public class ResultsCalculationResponse
    {
        public int TotalParticipants { get; set; }
        public int Finishers { get; set; }
        public int DNF { get; set; } // Did Not Finish
        public int Disqualified { get; set; }
        public long? FastestFinishTimeMs { get; set; }
        public long? SlowestFinishTimeMs { get; set; }
        public string? FastestFinishTimeFormatted { get; set; }
        public string? SlowestFinishTimeFormatted { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string Status { get; set; } = "Completed";
    }
}
