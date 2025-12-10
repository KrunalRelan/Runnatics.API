using Runnatics.Models.Client.Responses.Dashboard;

namespace Runnatics.Services.Interface
{
    public interface IDashboardService : ISimpleServiceBase
    {
        Task<DashboardStatsResponse> GetDashboardStats();
        Task<List<RecentActivityItem>> GetRecentActivity(int limit);
    }
}
