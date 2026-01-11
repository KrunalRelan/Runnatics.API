using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Heartbeat from R700 reader
    /// R700 sends this to: POST /api/rfid/heartbeat
    /// Used to monitor reader health and connectivity
    /// </summary>
    public class ReaderHeartbeatRequest
    {
        /// <summary>
        /// Serial number of the reader (required)
        /// </summary>
        [Required]
        [StringLength(100)]
        public string ReaderSerial { get; set; }

        /// <summary>
        /// Hostname of the reader
        /// </summary>
        [StringLength(100)]
        public string ReaderHostname { get; set; }

        /// <summary>
        /// Current IP address of the reader
        /// </summary>
        [StringLength(45)]
        public string IpAddress { get; set; }

        /// <summary>
        /// CPU temperature in Celsius
        /// </summary>
        public decimal? CpuTemperature { get; set; }

        /// <summary>
        /// Ambient temperature in Celsius
        /// </summary>
        public decimal? AmbientTemperature { get; set; }

        /// <summary>
        /// Firmware version
        /// Example: "7.0.0"
        /// </summary>
        [StringLength(50)]
        public string FirmwareVersion { get; set; }

        /// <summary>
        /// Reader uptime in seconds
        /// </summary>
        public long? UptimeSeconds { get; set; }

        /// <summary>
        /// Memory usage percentage
        /// </summary>
        public decimal? MemoryUsagePercent { get; set; }

        /// <summary>
        /// CPU usage percentage
        /// </summary>
        public decimal? CpuUsagePercent { get; set; }

        /// <summary>
        /// Status of each antenna
        /// </summary>
        public List<AntennaHeartbeat> Antennas { get; set; }
    }
}
