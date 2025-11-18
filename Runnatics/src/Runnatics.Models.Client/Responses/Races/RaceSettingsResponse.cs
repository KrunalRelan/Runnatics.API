using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Responses.Races
{
    public class RaceSettingsResponse
    {
        public int Id { get; set; }

        [Required]
        public int RaceId { get; set; }

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

        public string? DataHeaders { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
