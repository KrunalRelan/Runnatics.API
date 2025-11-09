namespace Runnatics.Models.Data.EventOrganizers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Entities;

    public class EventOrganizer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrganizationId { get; set; }

        [Required]
        [MaxLength(255)]
        public string OrganizerName { get; set; } = string.Empty;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}