namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// File upload formats
    /// </summary>
    public enum FileFormat
    {
        Unknown = 0,
        CSV = 1,
        JSON = 2,
        XML = 3,
        ImpinjCsv = 10,
        ImpinjJson = 11,
        ChronotrackCsv = 20,
        GenericCsv = 30,
        CustomJson = 31
    }
}
