namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Enumerations;
    using Runnatics.Models.Data.EventOrganizers;

    public class Event
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenantId { get; set; }

        [Required]
        public int EventOrganizerId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public DateTime EventDate { get; set; }

        [MaxLength(50)]
        public string TimeZone { get; set; } = "Asia/Kolkata";

        [MaxLength(255)]
        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public decimal? VenueLatitude { get; set; }
        public decimal? VenueLongitude { get; set; }

        public string EventType { get; set; } = string.Empty;

        //TODO: Add Zip Code

        public EventStatus Status { get; set; } = EventStatus.Active; 

        public int? MaxParticipants { get; set; }
        public DateTime? RegistrationDeadline { get; set; }
        public string? Settings { get; set; } // JSON

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual EventOrganizer EventOrganizer { get; set; } = null!;
        public virtual EventSettings? EventSettings { get; set; }
        public virtual LeaderboardSettings? LeaderboardSettings { get; set; }
        public virtual ICollection<Race> Races { get; set; } = [];
        public virtual ICollection<Checkpoint> Checkpoints { get; set; } = [];
        public virtual ICollection<Participant> Participants { get; set; } = [];
        public virtual ICollection<ChipAssignment> ChipAssignments { get; set; } = [];
        public virtual ICollection<ReadRaw> ReadRaws { get; set; } = [];
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = [];
        public virtual ICollection<SplitTime> SplitTimes { get; set; } = [];
        public virtual ICollection<Results> Results { get; set; } = [];
    }
}