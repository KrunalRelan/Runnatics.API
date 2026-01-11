using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    /// <summary>
    /// Response for heartbeat
    /// </summary>
    public class HeartbeatResponse
    {
        public bool Success { get; set; }
        public int? ReaderId { get; set; }
        public string ReaderName { get; set; }
        public int? AssignedCheckpointId { get; set; }
        public string AssignedCheckpointName { get; set; }
        public int? AssignedRaceId { get; set; }
        public DateTime ServerTime { get; set; } = DateTime.UtcNow;
    }
}
