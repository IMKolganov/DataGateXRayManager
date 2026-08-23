using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

namespace DataGateXRayManager.Services.Proxy;

public interface IActiveProxyConnectionService
{
    int Count { get; }
    void Add(ActiveProxyConnection connection);
    void Add(ActiveProxyConnection connection, string? commonName);
    bool Remove(string connectionId);
    bool TryGet(string connectionId, out ActiveProxyConnection? connection);
    string? GetCommonName(string connectionId);
    ActiveProxyConnection? TryGetByLocalProxy(int localProxyPort, string? host);
    IReadOnlyCollection<ActiveProxyConnection> GetAll();
    IReadOnlyList<ProxySessionHint> GetAllWithCommonNames();
}
