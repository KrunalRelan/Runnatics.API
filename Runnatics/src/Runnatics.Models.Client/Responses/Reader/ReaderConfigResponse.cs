using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    public class ReaderConfigResponse
    {
        public int? ProfileId { get; set; }
        public string ProfileName { get; set; }
        public int? CheckpointId { get; set; }
        public string CheckpointName { get; set; }
        public int? RaceId { get; set; }
        public string RaceName { get; set; }
        public int HeartbeatIntervalSeconds { get; set; } = 30;
        public int DuplicateFilterMs { get; set; } = 1000;
    }
}
