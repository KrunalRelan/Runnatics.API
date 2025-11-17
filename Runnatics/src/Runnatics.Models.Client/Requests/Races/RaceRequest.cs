using Runnatics.Models.Client.Requests.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Races
{
    public class RaceRequest
    {
        [Required]
        public int EventId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal Distance { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public int? MaxParticipants { get; set; }

        public RaceSettingsRequest? RaceSettings { get; set; }
    }
}
