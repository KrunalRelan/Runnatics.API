namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Status of file upload processing
    /// </summary>
    public enum FileProcessingStatus
    {
        Pending = 0,
        Validating = 1,
        Processing = 2,
        Completed = 3,
        PartiallyCompleted = 4,
        Failed = 5,
        Cancelled = 6
    }
}
