namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Status of individual read record processing
    /// </summary>
    public enum ReadRecordStatus
    {
        Pending = 0,
        Valid = 1,
        Duplicate = 2,
        InvalidEpc = 3,
        UnknownChip = 4,
        InvalidTimestamp = 5,
        OutOfRaceWindow = 6,
        Processed = 7,
        Skipped = 8
    }
}
