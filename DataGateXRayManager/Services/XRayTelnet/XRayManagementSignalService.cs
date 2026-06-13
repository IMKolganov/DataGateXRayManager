using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Services.XRayTelnet;

public class XRayManagementSignalService(XRayProcessApi xrayApi, ILogger<XRayManagementSignalService> logger)
{
    private readonly List<IMessageSubscriber> _subscribers = new();
    private readonly Lock _lock = new();

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            logger.LogInformation("Command is null or empty");
            return string.Empty;
        }

        try
        {
            logger.LogInformation("xray api command: {Command}", command);
            var parts = command.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries);
            var sub = parts[0].Trim();
            string? stdin = parts.Length > 1 ? parts[1].Trim() : null;
            return await xrayApi.RunApiAsync(sub, stdin, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "xray api failed");
            return $"Error: {ex.Message}";
        }
    }

    public void Subscribe(IMessageSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_lock)
            _subscribers.Add(subscriber);
    }

    public void Unsubscribe(IMessageSubscriber subscriber, string ip, int port)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_lock)
            _subscribers.Remove(subscriber);
    }

    internal void BroadcastLine(string line, CancellationToken ct)
    {
        List<IMessageSubscriber> snapshot;
        lock (_lock)
            snapshot = _subscribers.ToList();

        foreach (var s in snapshot)
        {
            try
            {
                _ = s.OnMessageReceived(line, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subscriber broadcast failed");
            }
        }
    }
}
