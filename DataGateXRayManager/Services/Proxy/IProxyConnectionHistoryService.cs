using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

namespace DataGateXRayManager.Services.Proxy;

public interface IProxyConnectionHistoryService
{
    int Count { get; }
    void Add(ProxyConnectionHistoryItem item);
    IReadOnlyCollection<ProxyConnectionHistoryItem> GetAll();
}
