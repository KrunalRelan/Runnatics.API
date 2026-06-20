using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class ManualTimeRequest
    {
        /// <summary>
        /// Wall-clock crossing date+time in the event's LOCAL timezone, with no offset
        /// (e.g. "2026-05-10T08:39:15"). Converted to UTC server-side via Event.TimeZone —
        /// the same conversion automatic reads use. Preferred over <see cref="FinishTimeMs"/>;
        /// carries the calendar date so midnight-crossing splits are unambiguous.
        /// </summary>
        public string? CrossingLocalDateTime { get; set; }

        /// <summary>
        /// Deprecated: elapsed milliseconds from race gun start. Retained for backward
        /// compatibility with older clients that send no date. Ignored when
        /// <see cref="CrossingLocalDateTime"/> is provided.
        /// </summary>
        public long? FinishTimeMs { get; set; }

        [Required]
        public string CheckpointId { get; set; }
    }
}
