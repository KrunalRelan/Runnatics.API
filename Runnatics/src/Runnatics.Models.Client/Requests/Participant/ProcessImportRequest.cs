namespace Runnatics.Models.Client.Requests.Participant
{
    public class ProcessImportRequest
    {
        public string ImportBatchId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string? RaceId { get; set; }

        /// <summary>Opt-in (default false): queue a "BIB assigned" SMS to each imported participant with a phone.</summary>
        public bool SendBibSms { get; set; }
    }
}