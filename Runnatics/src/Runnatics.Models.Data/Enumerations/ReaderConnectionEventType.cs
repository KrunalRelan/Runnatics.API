namespace Runnatics.Models.Data.Enumerations
{
    /// <summary>
    /// Reader connection event types
    /// </summary>
    public enum ReaderConnectionEventType
    {
        Connected = 0,
        Disconnected = 1,
        Reconnected = 2,
        ConnectionFailed = 3,
        Timeout = 4
    }
}
