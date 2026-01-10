namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// Request model for reprocessing a batch
    /// </summary>
    public class ReprocessBatchRequest
    {
        public int BatchId { get; set; }
        public bool ReprocessAll { get; set; } = false;
        public bool ReprocessErrors { get; set; } = true;
    }
}
