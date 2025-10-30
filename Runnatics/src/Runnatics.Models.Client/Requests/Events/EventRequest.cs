using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventRequest
    {
        public string Title { get; set; }
        public string Organizer { get; set; }
        public string City { get; set; }
        public DateTimeOffset Time { get; set; }
        public string TimeZone { get; set; }
        public string Details { get; set; }
        public string SmsText { get; set; }

        // Publishing info
        public bool IsPublished { get; set; }
        public bool IsConfirmed { get; set; }
        public bool RankOnNet { get; set; }

        // Leaderboard
        public string ResultBasis { get; set; } // "ChipTime" or "GunTime"
        public bool AllowOverall { get; set; }
        public bool AllowGenderCategory { get; set; }
    }
}
