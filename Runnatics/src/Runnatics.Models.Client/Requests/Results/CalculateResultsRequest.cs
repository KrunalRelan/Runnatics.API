using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Results
{
    public class CalculateResultsRequest
    {
        [Required(ErrorMessage = "Event ID is required")]
        public required string EventId { get; set; }

        [Required(ErrorMessage = "Race ID is required")]
        public required string RaceId { get; set; }

        /// <summary>
        /// If true, recalculates results even if they already exist
        /// </summary>
        public bool ForceRecalculation { get; set; } = false;

        /// <summary>
        /// If true, marks results as official
        /// </summary>
        public bool MarkAsOfficial { get; set; } = false;
    }
}
