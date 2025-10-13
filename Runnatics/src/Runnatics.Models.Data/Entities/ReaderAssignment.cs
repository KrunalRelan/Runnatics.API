namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class ReaderAssignment
    {
        public Guid EventId { get; set; }
        public Guid CheckpointId { get; set; }
        public Guid ReaderDeviceId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UnassignedAt { get; set; }
        public Guid? AssignedByUserId { get; set; }

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual User? AssignedByUser { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}