namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Reader alert type
    /// </summary>
    public enum ReaderAlertType
    {
        Offline = 1,
        HighTemperature = 2,
        LowReadRate = 3,
        AntennaDisconnected = 4,
        NetworkIssue = 5,
        MemoryFull = 6,
        FirmwareUpdateAvailable = 7
    }
}
