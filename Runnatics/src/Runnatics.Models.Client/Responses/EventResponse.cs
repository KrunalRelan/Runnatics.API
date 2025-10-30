using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses
{
    public class EventResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Organizer { get; set; }
        public string City { get; set; }
        public DateTimeOffset EventDateTime { get; set; }
        public string TimeZone { get; set; }
        public string Details { get; set; }
        public string SmsText { get; set; }
        public bool IsPublished { get; set; }
        public bool IsConfirmed { get; set; }
        public bool RankOnNet { get; set; }

        public string ResultBasis { get; set; }
        public bool AllowOverall { get; set; }
        public bool AllowGenderCategory { get; set; }
    }
}
