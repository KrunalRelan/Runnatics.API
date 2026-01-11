using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Reader registration request
    /// R700 sends this to: POST /api/rfid/register
    /// Called when reader first connects or configuration changes
    /// </summary>
    public class ReaderRegistrationRequest
    {
        /// <summary>
        /// Serial number of the reader (required)
        /// </summary>
        [Required]
        [StringLength(100)]
        public string SerialNumber { get; set; }

        /// <summary>
        /// Friendly name for the reader
        /// Example: "Start Line Reader"
        /// </summary>
        [StringLength(100)]
        public string Name { get; set; }

        /// <summary>
        /// Hostname of the reader
        /// </summary>
        [StringLength(100)]
        public string Hostname { get; set; }

        /// <summary>
        /// IP address of the reader
        /// </summary>
        [StringLength(45)]
        public string IpAddress { get; set; }

        /// <summary>
        /// MAC address of the reader
        /// Example: "00:16:25:12:DB:BF"
        /// </summary>
        [StringLength(17)]
        public string MacAddress { get; set; }

        /// <summary>
        /// Reader model
        /// Example: "Impinj R700"
        /// </summary>
        [StringLength(50)]
        public string Model { get; set; }

        /// <summary>
        /// Firmware version
        /// </summary>
        [StringLength(50)]
        public string FirmwareVersion { get; set; }

        /// <summary>
        /// Race ID to assign reader to
        /// </summary>
        public int? RaceId { get; set; }

        /// <summary>
        /// Checkpoint ID to assign reader to
        /// </summary>
        public int? CheckpointId { get; set; }
    }
}
