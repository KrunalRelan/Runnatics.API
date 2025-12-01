using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantsStagingRequest
    {
        public int ImportBatchId { get; set; }

        public int TenantId { get; set; }

        public int EventId { get; set; }

        public int RaceId { get; set; }

        public int UserId { get; set; }
    }
}
