namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// A single raw RFID detection from the reader hardware, including duplicates.
    /// Sourced from RawRFIDReading — every antenna ping, before deduplication/processing.
    /// </summary>
    public class RawRfidTagReading
    {
        /// <summary>Raw reading identifier (RawRFIDReading.Id)</summary>
        public long ReadingId { get; set; }

        /// <summary>RFID chip EPC identifier</summary>
        public string ChipId { get; set; } = string.Empty;

        /// <summary>Local timestamp as stored by the reader device</summary>
        public DateTime ReadTimeLocal { get; set; }

        /// <summary>UTC timestamp of the detection</summary>
        public DateTime ReadTimeUtc { get; set; }

        /// <summary>Checkpoint name this detection was assigned to (null if unassigned)</summary>
        public string? CheckpointName { get; set; }

        /// <summary>Device identifier (serial / MAC) that captured the reading</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Processing outcome: Pending, Success, Duplicate, Invalid
        /// </summary>
        public string ProcessResult { get; set; } = string.Empty;

        /// <summary>Whether this was a manual entry override</summary>
        public bool IsManualEntry { get; set; }

        /// <summary>RSSI signal strength (dBm)</summary>
        public decimal? RssiDbm { get; set; }

        /// <summary>Antenna port number on the reader</summary>
        public int? Antenna { get; set; }

        /// <summary>Additional notes or manual-entry reason</summary>
        public string? Notes { get; set; }
    }
}
