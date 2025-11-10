namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Enumerations;

    public class Event
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrganizationId { get; set; }

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

        public EventStatus Status { get; set; } = EventStatus.Draft;

        public int? MaxParticipants { get; set; }
        public DateTime? RegistrationDeadline { get; set; }
        public string? Settings { get; set; } // JSON

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual EventSettings? EventSettings { get; set; }
        public virtual LeaderboardSettings? LeaderboardSettings { get; set; }
        public virtual ICollection<RaceCategory> RaceCategories { get; set; } = new List<RaceCategory>();
        public virtual ICollection<Checkpoint> Checkpoints { get; set; } = new List<Checkpoint>();
        public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();
        public virtual ICollection<ChipAssignment> ChipAssignments { get; set; } = new List<ChipAssignment>();
        public virtual ICollection<ReadRaw> ReadRaws { get; set; } = new List<ReadRaw>();
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = new List<ReadNormalized>();
        public virtual ICollection<SplitTime> SplitTimes { get; set; } = new List<SplitTime>();
        public virtual ICollection<Results> Results { get; set; } = new List<Results>();
    }
}