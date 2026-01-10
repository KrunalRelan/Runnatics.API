namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// File processing status
    /// </summary>
    public enum FileProcessingStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        PartiallyCompleted = 4,
        Cancelled = 5
    }
}
