namespace DataGateXRayManager.Services.XRayTelnet;

public interface IMessageSubscriber
{
    Task OnMessageReceived(string message, CancellationToken cancellationToken);
}
