namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Read record processing status
    /// </summary>
    public enum ReadRecordStatus
    {
        Pending = 0,
        Processed = 1,
        Matched = 2,
        Duplicate = 3,
        Error = 4,
        UnknownChip = 5,
        Skipped = 6
    }
}
