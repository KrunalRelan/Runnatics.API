using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IFileProcessingService
    {
        Task ProcessBatchAsync(int batchId, CancellationToken cancellationToken = default);
        Task<List<ImpinjTagRead>> ParseFileAsync(int batchId);
    }
}
