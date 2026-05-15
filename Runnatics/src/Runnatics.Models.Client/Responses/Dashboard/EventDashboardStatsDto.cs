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
    }
}
