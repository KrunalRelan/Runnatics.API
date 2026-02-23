namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Detail of a single normalized RFID reading for a participant.
    /// Data sourced from ReadNormalized table.
    /// </summary>
    public class RfidReadingDetail
    {
        /// <summary>
        /// Unique reading identifier (ReadNormalized.Id)
        /// </summary>
        public int ReadingId { get; set; }

        /// <summary>
        /// UTC timestamp when the chip was read
        /// </summary>
        public DateTime ReadTimeUtc { get; set; }

        /// <summary>
        /// Chip time converted to the event's local timezone (formatted HH:mm:ss)
        /// </summary>
        public string? ReadTimeLocal { get; set; }

        /// <summary>
        /// Name of the checkpoint this reading belongs to
        /// </summary>
        public string? CheckpointName { get; set; }

        /// <summary>
        /// Name of the reader device at the checkpoint
        /// </summary>
        public string? DeviceName { get; set; }

        /// <summary>
        /// Gun time in milliseconds from race start
        /// </summary>
        public long? GunTimeMs { get; set; }

        /// <summary>
        /// Gun time formatted (e.g., "1:08:34")
        /// </summary>
        public string? GunTimeFormatted { get; set; }

        /// <summary>
        /// Net time in milliseconds from participant start
        /// </summary>
        public long? NetTimeMs { get; set; }

        /// <summary>
        /// Net time formatted (e.g., "1:08:10")
        /// </summary>
        public string? NetTimeFormatted { get; set; }

        /// <summary>
        /// RFID chip EPC identifier
        /// </summary>
        public string? ChipId { get; set; }

        /// <summary>
        /// Whether this reading was manually entered
        /// </summary>
        public bool IsManualEntry { get; set; }

        /// <summary>
        /// Reason for manual entry, if applicable
        /// </summary>
        public string? Notes { get; set; }
    }
}
