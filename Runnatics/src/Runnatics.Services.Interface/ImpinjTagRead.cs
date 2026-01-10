namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Represents a parsed RFID tag read from an uploaded file
    /// </summary>
    public class ImpinjTagRead
    {
        /// <summary>
        /// Electronic Product Code (EPC) of the RFID tag
        /// </summary>
        public string Epc { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the tag was read
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Antenna port number that detected the tag
        /// </summary>
        public int? AntennaPort { get; set; }

        /// <summary>
        /// Received Signal Strength Indicator in dBm
        /// </summary>
        public double? RssiDbm { get; set; }

        /// <summary>
        /// Serial number of the reader that detected the tag
        /// </summary>
        public string? ReaderSerialNumber { get; set; }

        /// <summary>
        /// Hostname of the reader
        /// </summary>
        public string? ReaderHostname { get; set; }

        /// <summary>
        /// Phase angle in degrees
        /// </summary>
        public double? PhaseAngleDegrees { get; set; }

        /// <summary>
        /// Doppler frequency shift in Hz
        /// </summary>
        public double? DopplerFrequencyHz { get; set; }

        /// <summary>
        /// RF channel index
        /// </summary>
        public int? ChannelIndex { get; set; }

        /// <summary>
        /// Peak RSSI in centidBm
        /// </summary>
        public int? PeakRssiCdBm { get; set; }

        /// <summary>
        /// Number of times the tag was seen in this read cycle
        /// </summary>
        public int TagSeenCount { get; set; } = 1;

        /// <summary>
        /// GPS latitude where the read occurred
        /// </summary>
        public double? GpsLatitude { get; set; }

        /// <summary>
        /// GPS longitude where the read occurred
        /// </summary>
        public double? GpsLongitude { get; set; }

        /// <summary>
        /// Additional custom data from the file
        /// </summary>
        public Dictionary<string, string>? CustomData { get; set; }
    }
}
