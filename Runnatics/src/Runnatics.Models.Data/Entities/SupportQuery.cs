using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class SupportQuery
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string SubmitterEmail { get; set; } = string.Empty;

        public int StatusId { get; set; }

        public int? QueryTypeId { get; set; }

        public int? AssignedToUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public virtual SupportQueryStatus Status { get; set; } = null!;
        public virtual SupportQueryType? QueryType { get; set; }
        public virtual User? AssignedToUser { get; set; }
        public virtual ICollection<SupportQueryComment> Comments { get; set; } = new List<SupportQueryComment>();
    }
}
