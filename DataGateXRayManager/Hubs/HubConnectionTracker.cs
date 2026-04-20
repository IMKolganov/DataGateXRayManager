namespace DataGateXRayManager.Hubs;

public class HubConnectionTracker
{
    private readonly object _sync = new();
    private int _eventHubConnections;
    private DateTime _lastHeartbeatUtc = DateTime.UtcNow;

    public void EventHubConnected(string connectionId)
    {
        lock (_sync)
            _eventHubConnections++;
    }

    public void EventHubDisconnected(string connectionId)
    {
        lock (_sync)
            _eventHubConnections = Math.Max(0, _eventHubConnections - 1);
    }

    public void TouchHeartbeat() => _lastHeartbeatUtc = DateTime.UtcNow;

    public int EventHubConnectionCount
    {
        get
        {
            lock (_sync)
                return _eventHubConnections;
        }
    }
}
