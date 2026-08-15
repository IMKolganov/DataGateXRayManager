using System.Collections.Concurrent;
using System.Net;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

namespace DataGateXRayManager.Services.Proxy;

public sealed class ActiveProxyConnectionService : IActiveProxyConnectionService
{
    private readonly ConcurrentDictionary<string, ActiveProxyConnection> _connections = new();
    private readonly ConcurrentDictionary<string, string> _commonNames = new(StringComparer.Ordinal);

    public int Count => _connections.Count;

    public void Add(ActiveProxyConnection connection) => Add(connection, commonName: null);

    public void Add(ActiveProxyConnection connection, string? commonName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.ConnectionId))
            throw new ArgumentException("ConnectionId is required.", nameof(connection));

        _connections[connection.ConnectionId] = connection;

        var cn = NormalizeCommonName(commonName);
        if (cn is null)
            _commonNames.TryRemove(connection.ConnectionId, out _);
        else
            _commonNames[connection.ConnectionId] = cn;
    }

    public bool Remove(string connectionId)
    {
        _commonNames.TryRemove(connectionId, out _);
        return _connections.TryRemove(connectionId, out _);
    }

    public bool TryGet(string connectionId, out ActiveProxyConnection? connection)
    {
        var ok = _connections.TryGetValue(connectionId, out var value);
        connection = value;
        return ok;
    }

    public string? GetCommonName(string connectionId) =>
        _commonNames.TryGetValue(connectionId, out var cn) ? cn : null;

    public ActiveProxyConnection? TryGetByLocalProxy(int localProxyPort, string? host)
    {
        var needle = NormalizeHost(host);
        foreach (var c in _connections.Values)
        {
            if (c.LocalProxyPort != localProxyPort)
                continue;
            if (HostsEqual(c.LocalProxyIp, needle))
                return c;
        }

        return null;
    }

    public IReadOnlyCollection<ActiveProxyConnection> GetAll() => _connections.Values.ToArray();

    public IReadOnlyList<ProxySessionHint> GetAllWithCommonNames()
    {
        var list = new List<ProxySessionHint>(_connections.Count);
        foreach (var c in _connections.Values)
        {
            _commonNames.TryGetValue(c.ConnectionId, out var cn);
            list.Add(new ProxySessionHint(c, cn));
        }

        return list;
    }

    private static bool HostsEqual(string? localProxyIp, string needleNormalized) =>
        string.Equals(NormalizeHost(localProxyIp), needleNormalized, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return NormalizeHost("127.0.0.1");

        var h = host.Trim();
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";

        if (!IPAddress.TryParse(h, out var ip))
            return h;

        if (IPAddress.IsLoopback(ip))
            return "127.0.0.1";

        return ip.ToString();
    }

    private static string? NormalizeCommonName(string? commonName)
    {
        if (string.IsNullOrWhiteSpace(commonName))
            return null;
        var trimmed = commonName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
