using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantRequest
    {
        [Required]
        public string BibNumber { get; set; } = string.Empty;

        [Required]
        public string RaceId { get; set; } = string.Empty;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address format")]
        public string? Email { get; set; }

        // ✅ Will validate ONLY when value is provided, allows null/empty
        [RegularExpression(@"^\+?[\d\s\-\(\)]{10,15}$",
            ErrorMessage = "Phone number must be 10-15 digits and can contain +, -, (), and spaces")]
        public string? Phone { get; set; }

        public string? Gender { get; set; }

        public string? Category { get; set; }

        public string? ChipId { get; set; }

        public bool CheckIn { get; set; }
    }
}
