// ============================================================================
// File: Hubs/RaceHub.cs
// Purpose: SignalR hub for real-time race events to React frontend.
// ============================================================================
using Microsoft.AspNetCore.SignalR;

namespace Runnatics.Hubs;

public class RaceHub : Hub
{
    public async Task JoinRace(string raceId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"race-{raceId}");

    public async Task LeaveRace(string raceId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"race-{raceId}");

    public async Task JoinDeviceMonitor()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "device-monitor");

    public override async Task OnDisconnectedAsync(Exception? exception)
        => await base.OnDisconnectedAsync(exception);
}
