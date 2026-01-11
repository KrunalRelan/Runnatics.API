namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Reader connection event type
    /// </summary>
    public enum ReaderConnectionEventType
    {
        Connected = 1,
        Disconnected = 2,
        Reconnecting = 3,
        Error = 4,
        HeartbeatReceived = 5,
        ConfigurationChanged = 6
    }
}
