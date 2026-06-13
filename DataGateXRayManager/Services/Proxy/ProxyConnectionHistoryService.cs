using System.Collections.Concurrent;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

namespace DataGateXRayManager.Services.Proxy;

public sealed class ProxyConnectionHistoryService : IProxyConnectionHistoryService
{
    private readonly ConcurrentQueue<ProxyConnectionHistoryItem> _items = new();

    public int Count => _items.Count;

    public void Add(ProxyConnectionHistoryItem item)
    {
        _items.Enqueue(item);
    }

    public IReadOnlyCollection<ProxyConnectionHistoryItem> GetAll()
    {
        return _items.ToArray();
    }
}
