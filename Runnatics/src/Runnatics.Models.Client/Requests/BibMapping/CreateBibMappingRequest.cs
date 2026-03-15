using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.BibMapping
{
    public class CreateBibMappingRequest
    {
        [Required]
        public string RaceId { get; set; } = string.Empty; // Encrypted

        [Required]
        [MaxLength(20)]
        public string BibNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Epc { get; set; } = string.Empty;
    }
}
