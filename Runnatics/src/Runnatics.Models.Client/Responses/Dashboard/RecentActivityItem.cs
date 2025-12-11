using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Dashboard
{
    public class RecentActivityItem
    {
        public string Id { get; set; } =  string.Empty;
        public string Type { get; set; }  = string.Empty; // "event_created", "participant_registered", etc.
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Icon { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
    }
}
