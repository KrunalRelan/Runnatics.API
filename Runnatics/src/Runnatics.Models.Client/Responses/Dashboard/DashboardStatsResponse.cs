using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Dashboard
{
    public class DashboardStatsResponse
    {
        public int? TotalEvents { get; set; }
        public int? TotalParticipants { get; set; }
        public int? TotalReports { get; set; }
        public decimal? GrowthPercentage { get; set; }

        // Optional: Additional stats
        public int? ActiveEvents { get; set; }
        public int? UpcomingEvents { get; set; }
        public int? CompletedEvents { get; set; }
    }
}
