using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class ProcessRFIDImportRequest
    {
        [Required(ErrorMessage = "Upload Batch ID is required")]
        public required string UploadBatchId { get; set; }  // Changed from ImportBatchId

        [Required(ErrorMessage = "Event ID is required")]
        public required string EventId { get; set; }

        [Required(ErrorMessage = "Race ID is required")]
        public required string RaceId { get; set; }

        /// <summary>
        /// Deduplication window in seconds (default: 3 seconds)
        /// </summary>
        public int DeduplicationWindowSeconds { get; set; } = 3;

        /// <summary>
        /// Auto-assign readings to checkpoints based on time gaps
        /// </summary>
        public bool AutoAssignCheckpoints { get; set; } = true;

        /// <summary>
        /// Minimum time gap (seconds) between checkpoints for auto-assignment
        /// </summary>
        public int MinCheckpointGapSeconds { get; set; } = 60;
    }
}
