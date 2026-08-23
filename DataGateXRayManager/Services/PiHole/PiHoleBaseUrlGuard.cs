using System.Net;
using System.Net.Sockets;

namespace DataGateXRayManager.Services.PiHole;

/// <summary>Rejects obviously dangerous Pi-hole BaseUrl values (SSRF to link-local / metadata).</summary>
public static class PiHoleBaseUrlGuard
{
    public static void EnsureSafeOrThrow(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Pi-hole BaseUrl is required.");

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Pi-hole BaseUrl is not a valid absolute URI: '{baseUrl}'.");

        if (uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"Pi-hole BaseUrl scheme must be http or https (got '{uri.Scheme}').");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("Pi-hole BaseUrl host is empty.");

        var host = uri.IdnHost;
        if (IsBlockedHostName(host))
        {
            throw new InvalidOperationException(
                $"Pi-hole BaseUrl host '{host}' is not allowed (cloud metadata / blocked name).");
        }

        if (IPAddress.TryParse(host, out var ip) && IsBlockedAddress(ip))
        {
            throw new InvalidOperationException(
                $"Pi-hole BaseUrl address '{ip}' is not allowed (link-local / metadata range).");
        }
    }

    private static bool IsBlockedHostName(string host) =>
        host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".metadata.google.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        // AWS/GCP/Azure link-local metadata
        if (ip.Equals(IPAddress.Parse("169.254.169.254")))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // Link-local 169.254.0.0/16 (except we already special-case metadata; block all link-local)
            if (b[0] == 169 && b[1] == 254)
                return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 link-local; fd00:ec2::254 style metadata often link-local adjacent — block link-local
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;
        }

        return false;
    }
}
