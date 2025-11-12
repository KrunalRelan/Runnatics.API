namespace Runnatics.Models.Client.Common
{
    /// <summary>
    /// Represents the possible statuses of an event
    /// </summary>
    public enum EventStatus
    {
        /// <summary>
        /// Event is in draft state and not yet published
        /// </summary>
        Draft = 0,

        /// <summary>
        /// Event is active and registration is open
        /// </summary>
        Active = 1,

        /// <summary>
        /// Event is currently in progress
        /// </summary>
        InProgress = 2,

        /// <summary>
        /// Event has been completed
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Event has been cancelled
        /// </summary>
        Cancelled = 4,

        Published = 5,
    }
}
