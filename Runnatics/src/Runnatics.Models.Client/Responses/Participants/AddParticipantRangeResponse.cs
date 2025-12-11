using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Participants
{
    public class AddParticipantRangeResponse
    {
        public int TotalCreated { get; set; }
        public int TotalSkipped { get; set; }
        public List<string> SkippedBibNumbers { get; set; } = [];
        public string Status { get; set; } = string.Empty;
    }
}
