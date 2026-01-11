using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    public class ReaderAssignmentRequest
    {
        public string SerialNumber { get; set; }
        public int? RaceId { get; set; }
        public int? CheckpointId { get; set; }
    }
}
