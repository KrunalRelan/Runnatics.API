using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantSearchRequest : SearchCriteriaBase
    {
        /// <summary>
        /// COMPUTED-status filter (contract 2026-07-07): a display-form STRING — "OK", "DNF",
        /// "DNS" or "DSQ" (case-insensitive; the stored spellings "Finished"/"DQ" are tolerated;
        /// null / empty / "all" = no filter). Matched against Results.Status — the value the
        /// grid's Status column displays — never the stale raw Participant.Status. Replaces the
        /// old numeric RaceStatus contract (1=Registered, 2=Finished, …) which filtered the raw
        /// participant field and returned wrong rows; deploy the UI bundle with this API.
        /// </summary>
        public string? Status { get; set; }

        public Gender? Gender { get; set; }

        public string? Category { get; set; }

    }
}
