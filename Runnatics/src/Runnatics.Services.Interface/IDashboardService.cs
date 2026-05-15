using Runnatics.Models.Client.Responses.Dashboard;

namespace Runnatics.Services.Interface
{
    public interface IDashboardService : ISimpleServiceBase
    {
        Task<DashboardStatsResponse> GetDashboardStats();
        Task<List<RecentActivityItem>> GetRecentActivity(int limit);
        Task<EventDashboardStatsDto?> GetEventDashboardStatsAsync(string eventId, CancellationToken ct);
        Task<RaceDashboardStatsDto?> GetRaceDashboardStatsAsync(string eventId, string raceId, CancellationToken ct);
    }
}
