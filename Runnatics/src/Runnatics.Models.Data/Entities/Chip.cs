using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Models.Data.Entities
{
    public class Chip
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int OrganizationId { get; set; }

        [Required]
        [MaxLength(50)]
        public string EPC { get; set; } = string.Empty; // Electronic Product Code

        [MaxLength(20)]
        public string Status { get; set; } = "Available"; // Available, Assigned, Lost, Damaged

        public int? BatteryLevel { get; set; } // Percentage
        public DateTime? LastSeenAt { get; set; }
        public string? Notes { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual ICollection<ChipAssignment> ChipAssignments { get; set; } = new List<ChipAssignment>();
    }
}