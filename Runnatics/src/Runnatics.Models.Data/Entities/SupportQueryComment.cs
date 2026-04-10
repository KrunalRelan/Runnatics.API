using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class SupportQueryComment
    {
        [Key]
        public int Id { get; set; }

        public int SupportQueryId { get; set; }

        [Required]
        public string CommentText { get; set; } = string.Empty;

        public int TicketStatusId { get; set; }

        public bool NotificationSent { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? CreatedByUserId { get; set; }

        // Navigation Properties
        public virtual SupportQuery SupportQuery { get; set; } = null!;
        public virtual SupportQueryStatus TicketStatus { get; set; } = null!;
        public virtual User? CreatedByUser { get; set; }
    }
}
