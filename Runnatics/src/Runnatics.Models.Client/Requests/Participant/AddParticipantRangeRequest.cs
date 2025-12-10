using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class AddParticipantRangeRequest
    {
        /// <summary>
        /// Optional prefix to prepend to bib numbers (e.g., "A" -> "A001")
        /// </summary>
        public string? Prefix { get; set; }

        /// <summary>
        /// Starting bib number in the range (required)
        /// </summary>
        [Required(ErrorMessage = "From Bib Number is required")]
        [Range(1, int.MaxValue, ErrorMessage = "From Bib Number must be greater than 0")]
        public int FromBibNumber { get; set; }

        /// <summary>
        /// Ending bib number in the range (required)
        /// </summary>
        [Required(ErrorMessage = "To Bib Number is required")]
        [Range(1, int.MaxValue, ErrorMessage = "To Bib Number must be greater than 0")]
        public int ToBibNumber { get; set; }

        /// <summary>
        /// Optional suffix to append to bib numbers (e.g., "X" -> "001X")
        /// </summary>
        public string? Suffix { get; set; }
    }
}
