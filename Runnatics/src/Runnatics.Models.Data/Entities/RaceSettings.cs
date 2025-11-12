using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class RaceSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RaceId { get; set; }

        public bool Published { get; set; }

        public bool SendSms { get; set; }

        public bool CheckValidation { get; set; }

        public bool ShowLeaderboard { get; set; }

        public bool ShowResultTable { get; set; }

        public bool IsTimed { get; set; }

        // Navigation Properties
        public virtual Race Race { get; set; } = null!;

        // Audit Properties
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
