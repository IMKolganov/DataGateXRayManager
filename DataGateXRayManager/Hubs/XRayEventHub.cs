using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Hubs;

public class XRayEventHub(HubConnectionTracker tracker, ILogger<XRayEventHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        tracker.EventHubConnected(Context.ConnectionId);
        logger.LogInformation("XRayEventHub connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.EventHubDisconnected(Context.ConnectionId);
        logger.LogInformation("XRayEventHub disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task Ping(string? instanceId = null)
    {
        tracker.TouchHeartbeat();
        logger.LogDebug("Heartbeat from EventHub client {Id} (instance={Instance})",
            Context.ConnectionId, instanceId ?? "n/a");
        return Task.CompletedTask;
    }
}
