namespace Runnatics.Models.Client.Responses.Dashboard
{
    public class EventDashboardStatsDto
    {
        public string EventId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public int TotalRegistered { get; set; }
        public int TotalFinishers { get; set; }
        public int TotalDnf { get; set; }
        public int TotalDns { get; set; }
        public List<GenderBreakdownItem> GenderBreakdown { get; set; } = [];
        public List<CategoryBreakdownItem> CategoryBreakdown { get; set; } = [];
        public List<RaceStatItem> RaceStats { get; set; } = [];

        /// <summary>
        /// Event-level headline totals — the exact sum of <see cref="RaceStats"/>, so the
        /// dashboard's top row can never disagree with the tiles beneath it.
        /// </summary>
        public EventRaceCountsDto Totals { get; set; } = new();
    }

    /// <summary>
    /// One numeric-dashboard column set. Statuses come from the SAME computed source the
    /// grid / export / public site read — the STORED Results.Status ("Finished", "DNF",
    /// "DNS", "DQ") surfaced under its display name (OK, DSQ) — NOT the legacy
    /// Participant.Status field.
    /// </summary>
    public class EventRaceCountsDto
    {
        /// <summary>Registered participants (active, not deleted).</summary>
        public int Registered { get; set; }

        /// <summary>Participants holding a live chip assignment (UnassignedAt IS NULL).</summary>
        public int EpcMapped { get; set; }

        /// <summary>Registered − EpcMapped.</summary>
        public int EpcNotMapped { get; set; }

        /// <summary>Results.Status == "Finished" (displayed as OK).</summary>
        public int FinishedOk { get; set; }

        public int Dnf { get; set; }
        public int Dns { get; set; }

        /// <summary>Results.Status == "DQ" (displayed as DSQ).</summary>
        public int Dsq { get; set; }

        /// <summary>
        /// Derived as Registered − (FinishedOk + Dnf + Dns + Dsq), so the buckets ALWAYS
        /// sum to Registered. Covers both "no Results row yet" and any stray stored status
        /// outside the four known values — neither can silently vanish from the dashboard.
        /// </summary>
        public int NotProcessed { get; set; }
    }

    public class RaceDashboardStatsDto
    {
        public string RaceId { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public int TotalRegistered { get; set; }
        public int TotalFinishers { get; set; }
        public int TotalDnf { get; set; }
        public int TotalDns { get; set; }
        public string? FastestFinishTime { get; set; }
        public string? AverageFinishTime { get; set; }
        public List<GenderBreakdownItem> GenderBreakdown { get; set; } = [];
        public List<CategoryBreakdownItem> CategoryBreakdown { get; set; } = [];
    }

    public class GenderBreakdownItem
    {
        public string Gender { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Finishers { get; set; }
    }

    public class CategoryBreakdownItem
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Finishers { get; set; }
    }

    public class RaceStatItem
    {
        public string RaceId { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public int Registered { get; set; }
        public int Finishers { get; set; }
        public int Dnf { get; set; }

        /// <summary>Per-race numeric dashboard. Same metric set as the event totals.</summary>
        public EventRaceCountsDto Counts { get; set; } = new();
    }
}
