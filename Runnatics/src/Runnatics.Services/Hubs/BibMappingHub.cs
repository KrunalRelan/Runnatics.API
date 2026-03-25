using Microsoft.AspNetCore.SignalR;

namespace Runnatics.Hubs;

public class BibMappingHub : Hub
{
    public async Task JoinBibMapping(string raceId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"bib-mapping-{raceId}");

    public async Task LeaveBibMapping(string raceId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bib-mapping-{raceId}");

    public Task<bool> GetReaderStatus()
    {
        // RfidReaderService sets this via static property; client can poll it
        return Task.FromResult(RfidReaderConnectionState.IsConnected);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
        => await base.OnDisconnectedAsync(exception);
}

/// <summary>
/// Shared static state for reader connection status, updated by RfidReaderService.
/// </summary>
public static class RfidReaderConnectionState
{
    public static volatile bool IsConnected;
}
