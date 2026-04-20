using DataGateXRayManager.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Services.XRayTelnet.Subscribers;

public class SignalRMessageSubscriber(IHubContext<XRaySignalHub> hubContext, string connectionId)
    : IMessageSubscriber
{
    public Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        return hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", message, cancellationToken);
    }
}
