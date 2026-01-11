using Runnatics.Models.Client.Reader;
using Runnatics.Models.Client.Responses.Reader;
using Runnatics.Models.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IRfidReaderService
    {
        /// <summary>
        /// Process a single tag read from R700 (online mode)
        /// </summary>
        Task<TagReadResponse> ProcessTagReadAsync(TagReadRequest request);

        /// <summary>
        /// Process a batch of tag reads from R700
        /// </summary>
        Task<TagReadBatchResponse> ProcessTagReadBatchAsync(TagReadBatchRequest request);

        /// <summary>
        /// Process reader heartbeat and update status
        /// </summary>
        Task<HeartbeatResponse> ProcessHeartbeatAsync(ReaderHeartbeatRequest request);

        /// <summary>
        /// Register a new reader or update existing
        /// </summary>
        Task<ReaderRegistrationResponse> RegisterReaderAsync(ReaderRegistrationRequest request);

        /// <summary>
        /// Get reader by serial number
        /// </summary>
        Task<ReaderDevice> GetReaderBySerialAsync(string serialNumber);

        /// <summary>
        /// Check if a read is duplicate
        /// </summary>
        Task<bool> IsDuplicateReadAsync(string epc, DateTime timestamp, int? checkpointId, int windowMs = 1000);
    }
}
