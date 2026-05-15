using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ChangeRaceCategoryRequest
    {
        [Required]
        public string AgeCategory { get; set; } = string.Empty;
    }
}
