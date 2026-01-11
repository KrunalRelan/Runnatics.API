using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Individual tag read item within a batch
    /// </summary>
    public class TagReadItem
    {
        /// <summary>
        /// EPC tag code (required)
        /// </summary>
        [Required]
        [StringLength(64)]
        public string Epc { get; set; }

        /// <summary>
        /// Timestamp when tag was read (required)
        /// </summary>
        [Required]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Antenna port number (1-4)
        /// </summary>
        public int? AntennaPort { get; set; }

        /// <summary>
        /// RSSI signal strength in dBm
        /// </summary>
        public double? Rssi { get; set; }

        /// <summary>
        /// Channel index
        /// </summary>
        public int? Channel { get; set; }

        /// <summary>
        /// Number of times tag was seen
        /// </summary>
        public int? TagSeenCount { get; set; }
    }
}
