using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    /// <summary>
    /// Response for batch tag reads
    /// </summary>
    public class TagReadBatchResponse
    {
        public bool Success { get; set; }
        public int TotalReceived { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalDuplicates { get; set; }
        public int TotalMatched { get; set; }
        public int TotalErrors { get; set; }
        public List<TagReadResultItem> Results { get; set; } = [];
    }
}
