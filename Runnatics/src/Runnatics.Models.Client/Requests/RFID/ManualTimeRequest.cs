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

        /// <summary>
        /// "Choose which raw read is the crossing" support. When set, the override is a CHOSEN READ:
        /// the id of an existing <c>RawRFIDReading</c> at this checkpoint that the operator selected as
        /// the crossing (the panel surfaces this as the read's plain numeric Id). The server uses that
        /// read's UTC time as the crossing — <see cref="CrossingLocalDateTime"/>/<see cref="FinishTimeMs"/>
        /// are ignored. The read must be assigned to <see cref="CheckpointId"/> and belong to this
        /// participant's chip, else the request is rejected (400). NULL = typed manual time (legacy).
        /// </summary>
        public string? ChosenRawReadId { get; set; }

        [Required]
        public string CheckpointId { get; set; }
    }
}
