using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

namespace DataGateXRayManager.Services.Proxy;

public interface IActiveProxyConnectionService
{
    int Count { get; }
    void Add(ActiveProxyConnection connection);
    bool Remove(string connectionId);
    bool TryGet(string connectionId, out ActiveProxyConnection? connection);
    ActiveProxyConnection? TryGetByLocalProxy(int localProxyPort, string? host);
    IReadOnlyCollection<ActiveProxyConnection> GetAll();
}
