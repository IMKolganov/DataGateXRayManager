using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

namespace DataGateXRayManager.Services.XRayServices;

public interface IXRayActiveSessionsService
{
    Task<XrayClientsEnvelope> GetActiveClientsAsync(CancellationToken cancellationToken);
}
