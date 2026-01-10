namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// DTO for batch list with pagination
    /// </summary>
    public class FileUploadBatchListDto
    {
        public List<FileUploadStatusDto> Batches { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
