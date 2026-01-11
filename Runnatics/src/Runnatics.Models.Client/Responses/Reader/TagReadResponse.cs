using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    /// <summary>
    /// Response for single tag read
    /// </summary>
    public class TagReadResponse
    {
        public bool Success { get; set; }
        public long? ReadRawId { get; set; }
        public string Message { get; set; }
        public bool IsDuplicate { get; set; }
        public int? MatchedChipId { get; set; }
        public int? MatchedParticipantId { get; set; }
        public string MatchedBibNumber { get; set; }
    }
}
