namespace Runnatics.Models.Client.Requests.Races
{
    public class RaceSettingsRequest
    {
        public bool Published { get; set; }

        public bool SendSms { get; set; }

        public bool CheckValidation { get; set; }

        public bool ShowLeaderboard { get; set; }

        public bool ShowResultTable { get; set; }

        public bool IsTimed { get; set; }

        public int? DedUpSeconds { get; set; }

        public int? EarlyStartCutOff { get; set; }

        public int? LateStartCutOff { get; set; }

        public bool? HasLoops { get; set; }

        public decimal? LoopLength { get; set; }

        public string? DataHeader { get; set; }
    }
}
