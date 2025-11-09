using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    public class RaceCategory
    {
        [Key]
        public int Id { get; set; } 
        
        [Required]
        public int EventId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "5K", "10K", "Half Marathon"


        public decimal DistanceKm { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? CutoffTime { get; set; }
        public int? MaxParticipants { get; set; }
        public decimal? EntryFee { get; set; }
        public int AgeMin { get; set; } = 0;
        public int AgeMax { get; set; } = 120;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        [MaxLength(20)]
        public string? GenderRestriction { get; set; } // "Male", "Female", null for open

        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();
        public virtual ICollection<Results> Results { get; set; } = new List<Results>();
    }
}