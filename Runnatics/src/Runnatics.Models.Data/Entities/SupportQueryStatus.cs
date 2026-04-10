using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class SupportQueryStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        // Navigation Properties
        public virtual ICollection<SupportQuery> SupportQueries { get; set; } = new List<SupportQuery>();
        public virtual ICollection<SupportQueryComment> SupportQueryComments { get; set; } = new List<SupportQueryComment>();
    }
}
