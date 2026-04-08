using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class SupportQueryType
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Navigation Properties
        public virtual ICollection<SupportQuery> SupportQueries { get; set; } = new List<SupportQuery>();
    }
}
