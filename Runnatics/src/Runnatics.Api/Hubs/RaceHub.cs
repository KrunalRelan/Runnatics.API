using Microsoft.AspNetCore.SignalR;
using Runnatics.Services.Interface.Hubs;

namespace Runnatics.Api.Hubs
{
    /// <summary>
    /// SignalR hub for real-time race updates including file uploads, 
    /// reader health, and RFID read notifications
    /// </summary>
    public class RaceHub : Hub<IRaceHubClient>
    {
        private readonly ILogger<RaceHub> _logger;

        public RaceHub(ILogger<RaceHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Called when a client connects to the hub
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a client disconnects from the hub
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Allows a client to join a race-specific group to receive updates for that race
        /// </summary>
        /// <param name="raceId">The race ID to join</param>
        public async Task JoinRace(int raceId)
        {
            var groupName = SignalRGroupNames.GetRaceGroupName(raceId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} joined race group {RaceId}", Context.ConnectionId, raceId);
        }

        /// <summary>
        /// Allows a client to leave a race-specific group
        /// </summary>
        /// <param name="raceId">The race ID to leave</param>
        public async Task LeaveRace(int raceId)
        {
            var groupName = SignalRGroupNames.GetRaceGroupName(raceId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} left race group {RaceId}", Context.ConnectionId, raceId);
        }

        /// <summary>
        /// Allows a client to join an event-specific group to receive updates for all races in that event
        /// </summary>
        /// <param name="eventId">The event ID to join</param>
        public async Task JoinEvent(int eventId)
        {
            var groupName = SignalRGroupNames.GetEventGroupName(eventId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} joined event group {EventId}", Context.ConnectionId, eventId);
        }

        /// <summary>
        /// Allows a client to leave an event-specific group
        /// </summary>
        /// <param name="eventId">The event ID to leave</param>
        public async Task LeaveEvent(int eventId)
        {
            var groupName = SignalRGroupNames.GetEventGroupName(eventId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} left event group {EventId}", Context.ConnectionId, eventId);
        }

        /// <summary>
        /// Allows a client to subscribe to reader health updates
        /// </summary>
        public async Task SubscribeToReaderHealth()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroupNames.ReaderHealth);
            _logger.LogInformation("Client {ConnectionId} subscribed to reader health updates", Context.ConnectionId);
        }

        /// <summary>
        /// Allows a client to unsubscribe from reader health updates
        /// </summary>
        public async Task UnsubscribeFromReaderHealth()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SignalRGroupNames.ReaderHealth);
            _logger.LogInformation("Client {ConnectionId} unsubscribed from reader health updates", Context.ConnectionId);
        }
    }
}
