using System.Globalization;
using System.Net;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

namespace DataGateXRayManager.Services.Proxy;

/// <summary>Active proxy socket plus optional VLESS email / common name from <c>clientRef</c>.</summary>
public readonly record struct ProxySessionHint(ActiveProxyConnection Connection, string? CommonName);

/// <summary>
/// When Xray-core reports a loopback/private peer IP (WSS/docker proxy hop), overlays
/// <see cref="XrayClientSessionDto.ProxyRealIp"/> from in-memory proxy sessions.
/// Matching prefers LocalProxy host(+port) equal to Xray peer; CN only disambiguates duplicates.
/// Never match by clientRef alone — /api/proxy is unauthenticated.
/// </summary>
public static class XRayProxyRealIpEnricher
{
    public static void Enrich(IList<XrayClientSessionDto> clients, IReadOnlyList<ProxySessionHint> proxySessions)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(proxySessions);

        if (clients.Count == 0 || proxySessions.Count == 0)
            return;

        var candidates = clients
            .Where(c => NeedsProxyEnrichment(c.RemoteAddress) || string.IsNullOrWhiteSpace(c.RemoteAddress))
            .ToList();
        if (candidates.Count == 0)
            return;

        var withUsableRealIp = proxySessions
            .Where(s => HasUsablePublicRealClientIp(s.Connection))
            .ToList();
        if (withUsableRealIp.Count == 0)
            return;

        foreach (var client in candidates)
        {
            var match = ResolveProxy(client, withUsableRealIp);
            if (match is null)
                continue;

            client.ProxyRealIp = FormatProxyRealIp(
                match.Value.Connection.RealClientIp,
                match.Value.Connection.RealClientPort);
        }
    }

    public static void Enrich(IList<XrayClientSessionDto> clients, IActiveProxyConnectionService proxies)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        Enrich(clients, proxies.GetAllWithCommonNames());
    }

    public static bool NeedsProxyEnrichment(string? remoteAddress)
    {
        if (!TryParseHostPort(remoteAddress, out var host, out _))
            return false;

        if (!IPAddress.TryParse(host, out var ip))
            return false;

        return IsPrivateOrLoopback(ip);
    }

    public static bool HasUsablePublicRealClientIp(ActiveProxyConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.RealClientIp))
            return false;

        if (!TryParseHostPort(connection.RealClientIp, out var host, out _)
            && !IPAddress.TryParse(connection.RealClientIp.Trim(), out _))
            return false;

        var hostOnly = TryParseHostPort(connection.RealClientIp, out var h, out _)
            ? h
            : connection.RealClientIp.Trim();

        if (!IPAddress.TryParse(hostOnly, out var ip))
            return true; // hostname — allow

        return !IsPrivateOrLoopback(ip);
    }

    public static string? FormatProxyRealIp(string? ip, int port)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        var trimmed = ip.Trim();
        if (TryParseHostPort(trimmed, out var host, out _) || IPAddress.TryParse(trimmed, out _))
        {
            var hostPart = TryParseHostPort(trimmed, out var h, out _) ? h : trimmed;
            if (IPAddress.TryParse(hostPart, out var parsed) && IsPrivateOrLoopback(parsed))
                return null;
        }

        if (port is > 0 and <= 65535)
            return $"{trimmed}:{port.ToString(CultureInfo.InvariantCulture)}";

        return trimmed;
    }

    public static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || bytes[0] == 127
                   || (bytes[0] == 169 && bytes[1] == 254)
                   || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;
        }

        return false;
    }

    private static ProxySessionHint? ResolveProxy(
        XrayClientSessionDto client,
        IReadOnlyList<ProxySessionHint> withUsableRealIp)
    {
        _ = TryParseHostPort(client.RemoteAddress, out var remoteHost, out var remotePort);

        // LocalProxyIp is the outbound source toward Xray — it must equal the peer host Xray reports.
        // Never match by clientRef/CN alone: /api/proxy is unauthenticated and clientRef/XFF are spoofable.
        // CN only disambiguates when host(+port) already matched more than one socket.

        // 1) Exact host + port (strongest).
        if (remotePort is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(remoteHost))
        {
            var hostNorm = ActiveProxyConnectionService.NormalizeHost(remoteHost);
            var byHostPort = withUsableRealIp
                .Where(s => s.Connection.LocalProxyPort == remotePort
                            && string.Equals(
                                ActiveProxyConnectionService.NormalizeHost(s.Connection.LocalProxyIp),
                                hostNorm,
                                StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Connection.ConnectedAtUtc)
                .ToList();
            if (byHostPort.Count == 1)
                return byHostPort[0];
            if (byHostPort.Count > 1 && !string.IsNullOrWhiteSpace(client.Email))
            {
                var byHostPortAndCn = byHostPort
                    .Where(s => CommonNamesEqual(s.CommonName, client.Email))
                    .ToList();
                if (byHostPortAndCn.Count == 1)
                    return byHostPortAndCn[0];
            }
        }

        // 2) Unique LocalProxy host == remote host (Xray often omits peer port).
        if (!string.IsNullOrWhiteSpace(remoteHost))
        {
            var hostNorm = ActiveProxyConnectionService.NormalizeHost(remoteHost);
            var byHost = withUsableRealIp
                .Where(s => string.Equals(
                    ActiveProxyConnectionService.NormalizeHost(s.Connection.LocalProxyIp),
                    hostNorm,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Connection.ConnectedAtUtc)
                .ToList();
            if (byHost.Count == 1)
                return byHost[0];
            if (byHost.Count > 1 && remotePort is > 0 and <= 65535)
            {
                var byHostThenPort = byHost
                    .Where(s => s.Connection.LocalProxyPort == remotePort)
                    .ToList();
                if (byHostThenPort.Count == 1)
                    return byHostThenPort[0];
            }

            if (byHost.Count > 1 && !string.IsNullOrWhiteSpace(client.Email))
            {
                var byHostAndCn = byHost
                    .Where(s => CommonNamesEqual(s.CommonName, client.Email))
                    .ToList();
                if (byHostAndCn.Count == 1)
                    return byHostAndCn[0];
            }
        }

        return null;
    }

    private static bool CommonNamesEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool TryParseHostPort(string? remoteAddress, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(remoteAddress))
            return false;

        var s = remoteAddress.Trim();
        if (IPEndPoint.TryParse(s, out var ep))
        {
            host = ep.Address.ToString();
            port = ep.Port;
            return true;
        }

        if (IPAddress.TryParse(s, out var ipOnly))
        {
            host = ipOnly.ToString();
            return true;
        }

        var lastColon = s.LastIndexOf(':');
        if (lastColon <= 0 || lastColon >= s.Length - 1)
            return false;

        var hostPart = s[..lastColon];
        if (hostPart.StartsWith('[') && hostPart.EndsWith(']'))
            hostPart = hostPart[1..^1];

        if (!int.TryParse(s[(lastColon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            return false;

        if (port is < 1 or > 65535)
            return false;

        host = hostPart;
        return host.Length > 0;
    }
}
