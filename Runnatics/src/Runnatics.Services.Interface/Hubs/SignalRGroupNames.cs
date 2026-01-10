namespace Runnatics.Services.Interface.Hubs
{
    /// <summary>
    /// Helper class for SignalR group naming conventions
    /// </summary>
    public static class SignalRGroupNames
    {
        /// <summary>
        /// Gets the group name for a specific race
        /// </summary>
        public static string GetRaceGroupName(int raceId) => $"Race_{raceId}";

        /// <summary>
        /// Gets the group name for a specific event
        /// </summary>
        public static string GetEventGroupName(int eventId) => $"Event_{eventId}";

        /// <summary>
        /// The group name for reader health updates
        /// </summary>
        public const string ReaderHealth = "ReaderHealth";
    }
}
