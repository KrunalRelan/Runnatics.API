using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IFileUploadService
    {
        Task<FileUploadResponse> UploadFileAsync(IFormFile file, FileUploadRequest request, int userId);
        Task<FileUploadStatusDto> GetBatchStatusAsync(int batchId);
        Task<FileUploadStatusDto?> GetBatchStatusByGuidAsync(Guid batchGuid);
        Task<FileUploadBatchListDto> GetBatchesAsync(int raceId, int pageNumber = 1, int pageSize = 20);
        Task<List<FileUploadRecordDto>> GetBatchRecordsAsync(int batchId, int pageNumber = 1, int pageSize = 100);
        Task<bool> CancelBatchAsync(int batchId);
        Task<bool> ReprocessBatchAsync(ReprocessBatchRequest request);
        Task<bool> DeleteBatchAsync(int batchId);
    }
}
