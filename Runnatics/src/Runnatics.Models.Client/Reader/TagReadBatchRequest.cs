using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Batch of tag reads from R700 reader
    /// R700 sends this to: POST /api/rfid/reads/batch
    /// More efficient when reader buffers multiple reads
    /// </summary>
    public class TagReadBatchRequest
    {
        /// <summary>
        /// Serial number of the reader (required)
        /// </summary>
        [Required]
        [StringLength(100)]
        public string ReaderSerial { get; set; }

        /// <summary>
        /// List of tag reads
        /// </summary>
        [Required]
        public List<TagReadItem> Reads { get; set; } = new();
    }
}
