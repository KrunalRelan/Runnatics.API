using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Single tag read from R700 reader (online mode)
    /// R700 sends this to: POST /api/rfid/read
    /// </summary>
    public class TagReadRequest
    {
        /// <summary>
        /// EPC tag code (required)
        /// Example: "E2003412012345678"
        /// </summary>
        [Required]
        [StringLength(64)]
        public string Epc { get; set; }

        /// <summary>
        /// Timestamp when tag was read (required)
        /// Example: "2024-01-15T08:30:45.123Z"
        /// </summary>
        [Required]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Antenna port number (1-4 for R700)
        /// </summary>
        public int? AntennaPort { get; set; }

        /// <summary>
        /// RSSI signal strength in dBm
        /// Example: -45.5
        /// </summary>
        public double? Rssi { get; set; }

        /// <summary>
        /// Serial number of the reader
        /// Example: "R700-001"
        /// </summary>
        [StringLength(100)]
        public string ReaderSerial { get; set; }

        /// <summary>
        /// Hostname of the reader
        /// </summary>
        [StringLength(100)]
        public string ReaderHostname { get; set; }

        /// <summary>
        /// Channel index used for the read
        /// </summary>
        public int? Channel { get; set; }

        /// <summary>
        /// Phase angle in degrees
        /// </summary>
        public double? PhaseAngle { get; set; }

        /// <summary>
        /// Doppler frequency in Hz (indicates tag movement)
        /// </summary>
        public double? Doppler { get; set; }

        /// <summary>
        /// Number of times tag was seen (aggregated reads)
        /// </summary>
        public int? TagSeenCount { get; set; }

        /// <summary>
        /// GPS latitude (if reader has GPS)
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>
        /// GPS longitude (if reader has GPS)
        /// </summary>
        public double? Longitude { get; set; }
    }
}
