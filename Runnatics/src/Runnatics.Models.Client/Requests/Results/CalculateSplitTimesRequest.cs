using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Results
{
    public class CalculateSplitTimesRequest
    {
        [Required(ErrorMessage = "Event ID is required")]
        public required string EventId { get; set; }

        [Required(ErrorMessage = "Race ID is required")]
        public required string RaceId { get; set; }

        /// <summary>
        /// If true, recalculates split times even if they already exist
        /// </summary>
        public bool ForceRecalculation { get; set; } = false;
    }
}
