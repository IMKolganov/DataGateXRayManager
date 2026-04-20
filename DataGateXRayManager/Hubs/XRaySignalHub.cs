using System.Collections.Concurrent;
using DataGateXRayManager.Services.XRayTelnet;
using DataGateXRayManager.Services.XRayTelnet.Subscribers;
using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Hubs;

public class XRaySignalHub(
    XRayManagementSignalService vpnService,
    ILogger<XRaySignalHub> logger) : Hub
{
    private static readonly ConcurrentDictionary<string, SignalRMessageSubscriber> Subscribers = new();

    public override async Task OnConnectedAsync()
    {
        var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        logger.LogInformation("XRaySignalHub connected: {ConnectionId}, RemoteIP={RemoteIp}", Context.ConnectionId,
            remoteIp);

        var hubCtx = Context.GetHttpContext()!.RequestServices.GetRequiredService<IHubContext<XRaySignalHub>>();
        var subscriber = new SignalRMessageSubscriber(hubCtx, Context.ConnectionId);

        if (Subscribers.TryAdd(Context.ConnectionId, subscriber))
        {
            vpnService.Subscribe(subscriber);
            logger.LogInformation("Subscriber registered for ConnectionId={ConnectionId}. Total={Total}",
                Context.ConnectionId, Subscribers.Count);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Subscribers.TryRemove(Context.ConnectionId, out var subscriber))
        {
            try
            {
                vpnService.Unsubscribe(subscriber, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown", 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unsubscribe failed for ConnectionId={ConnectionId}", Context.ConnectionId);
            }

            logger.LogInformation("XRaySignalHub disconnected: {ConnectionId}. Total={Total}", Context.ConnectionId,
                Subscribers.Count);
        }

        if (exception != null)
            logger.LogWarning(exception, "Disconnect error for {ConnectionId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public Task Ping(string? instanceId = null)
    {
        logger.LogDebug("Heartbeat from {ConnectionId}, instance={InstanceId}", Context.ConnectionId,
            instanceId ?? "n/a");
        return Task.CompletedTask;
    }

    public async Task SendCommand(string command)
    {
        logger.LogInformation("SendCommand from {ConnectionId}: {Command}", Context.ConnectionId, command);
        try
        {
            var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);
            await Clients.All.SendAsync("ReceiveCommandResult", result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendCommand failed for {ConnectionId}", Context.ConnectionId);
            throw new HubException(ex.Message);
        }
    }

    public async Task SendCommandWithRequestId(string requestId, string command)
    {
        logger.LogInformation("SendCommandWithRequestId from {ConnectionId}, RequestId={RequestId}: {Command}",
            Context.ConnectionId, requestId, command);
        try
        {
            var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveCommandResultWithRequestId", requestId, result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendCommandWithRequestId failed for {ConnectionId}, RequestId={RequestId}",
                Context.ConnectionId, requestId);
            throw new HubException(ex.Message);
        }
    }
}
