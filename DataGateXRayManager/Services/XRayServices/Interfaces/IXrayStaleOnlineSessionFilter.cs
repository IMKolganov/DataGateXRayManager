using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

namespace DataGateXRayManager.Services.XRayServices;

public interface IXrayStaleOnlineSessionFilter
{
    /// <summary>
    /// Keeps the sessions that still look alive and drops online-map leftovers. Stateful across polls:
    /// traffic counters are compared with the previous observation, so the instance must be a singleton.
    /// </summary>
    List<XrayClientSessionDto> RetainActive(
        IReadOnlyList<XrayOnlineClientObservation> observations,
        DateTimeOffset nowUtc);
}
