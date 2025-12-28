using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Participant
{
    /// <summary>
    /// Request model for updating existing participants by matching bib numbers from uploaded CSV
    /// </summary>
    public class UpdateParticipantsByBibRequest
    {
        /// <summary>
        /// The CSV file containing participant details with bib numbers
        /// </summary>
        [Required(ErrorMessage = "File is required")]
        public required IFormFile File { get; set; }
    }
}
