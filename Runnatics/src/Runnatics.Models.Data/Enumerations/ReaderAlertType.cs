namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Reader alert types
    /// </summary>
    public enum ReaderAlertType
    {
        Offline = 0,
        HighTemperature = 1,
        LowSignal = 2,
        AntennaError = 3,
        FirmwareOutdated = 4,
        HighCpuUsage = 5,
        HighMemoryUsage = 6,
        ConnectionError = 7
    }
}
