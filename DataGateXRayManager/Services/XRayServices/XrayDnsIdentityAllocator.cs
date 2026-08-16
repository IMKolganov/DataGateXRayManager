using System.Net;
using System.Net.Sockets;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>Allocates unique identity IPs from <c>XRAY_DNS_IDENTITY_SUBNET</c> (OpenVPN VirtualAddress analogue).</summary>
public static class XrayDnsIdentityAllocator
{
    public const string DefaultSubnetCidr = "10.80.0.0/24";

    /// <summary>
    /// Next free host address in the subnet. Skips network, broadcast, and .1 (reserved).
    /// Returns null if the pool is exhausted.
    /// </summary>
    public static string? AllocateNext(string? subnetCidr, IEnumerable<string?> usedIps)
    {
        var cidr = string.IsNullOrWhiteSpace(subnetCidr) ? DefaultSubnetCidr : subnetCidr.Trim();
        if (!TryParseCidr(cidr, out var network, out var prefixLength))
            throw new ArgumentException($"Invalid identity subnet CIDR: '{cidr}'.", nameof(subnetCidr));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ip in usedIps)
        {
            if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip.Trim(), out var parsed))
                used.Add(parsed.ToString());
        }

        foreach (var candidate in EnumerateAssignableHosts(network, prefixLength))
        {
            var s = candidate.ToString();
            if (!used.Contains(s))
                return s;
        }

        return null;
    }

    /// <summary>Assign missing IdentityIp on active clients; clear IdentityIp on revoked. Returns true if store mutated.</summary>
    public static bool EnsureIdentityIps(IList<StoredXRayClient> store, string? subnetCidr)
    {
        ArgumentNullException.ThrowIfNull(store);
        var changed = false;

        foreach (var c in store.Where(x => x.IsRevoked && !string.IsNullOrWhiteSpace(x.IdentityIp)))
        {
            c.IdentityIp = null;
            changed = true;
        }

        var used = store
            .Where(c => !c.IsRevoked && !string.IsNullOrWhiteSpace(c.IdentityIp))
            .Select(c => c.IdentityIp)
            .ToList();

        foreach (var c in store.Where(x => !x.IsRevoked && string.IsNullOrWhiteSpace(x.IdentityIp)))
        {
            var next = AllocateNext(subnetCidr, used);
            if (next is null)
                throw new InvalidOperationException(
                    $"DNS identity IP pool exhausted for subnet '{subnetCidr ?? DefaultSubnetCidr}'.");

            c.IdentityIp = next;
            used.Add(next);
            changed = true;
        }

        return changed;
    }

    public static string? FindCommonNameByIdentityIp(IEnumerable<StoredXRayClient> store, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
            return null;

        var host = StripPort(clientIp.Trim());
        if (!IPAddress.TryParse(host, out var needle))
            return null;

        var needleStr = needle.ToString();
        var matches = store
            .Where(c => !c.IsRevoked && !string.IsNullOrWhiteSpace(c.IdentityIp))
            .Where(c =>
                IPAddress.TryParse(c.IdentityIp, out var ip) &&
                string.Equals(ip.ToString(), needleStr, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.CommonName)
            .Where(cn => !string.IsNullOrWhiteSpace(cn))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // Ambiguous / corrupted store: refuse attribution rather than pick the first CN.
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Suggested Pi-hole <c>ClientSubnetPrefix</c> for a /24-style identity pool (e.g. <c>10.80.1.0/24</c> → <c>10.80.1.</c>).
    /// </summary>
    public static string? SuggestedClientSubnetPrefix(string? subnetCidr)
    {
        var cidr = string.IsNullOrWhiteSpace(subnetCidr) ? DefaultSubnetCidr : subnetCidr.Trim();
        if (!TryParseCidr(cidr, out var network, out var prefixLength) || prefixLength > 24)
            return null;

        var bytes = network.GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
    }

    /// <summary>
    /// True when at least one assignable identity host would pass the Pi-hole ClientSubnetPrefix filter.
    /// Empty prefix never matches (Xray requires an explicit prefix).
    /// </summary>
    public static bool ClientSubnetPrefixCoversIdentityPool(string? clientSubnetPrefix, string? subnetCidr)
    {
        if (string.IsNullOrWhiteSpace(clientSubnetPrefix))
            return false;

        var prefix = clientSubnetPrefix.Trim();
        var cidr = string.IsNullOrWhiteSpace(subnetCidr) ? DefaultSubnetCidr : subnetCidr.Trim();
        if (!TryParseCidr(cidr, out var network, out var prefixLength))
            return false;

        return EnumerateAssignableHosts(network, prefixLength)
            .Any(ip => ip.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryParseCidr(string cidr, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;
        var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out network!) ||
            !int.TryParse(parts[1], out prefixLength) ||
            prefixLength is < 0 or > 32 ||
            network.AddressFamily != AddressFamily.InterNetwork)
        {
            network = IPAddress.None;
            prefixLength = 0;
            return false;
        }

        return true;
    }

    internal static IEnumerable<IPAddress> EnumerateAssignableHosts(IPAddress network, int prefixLength)
    {
        if (network.AddressFamily != AddressFamily.InterNetwork)
            yield break;

        var netBytes = network.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(netBytes);
        var netUint = BitConverter.ToUInt32(netBytes, 0);
        var hostBits = 32 - prefixLength;
        if (hostBits <= 0)
            yield break;

        var hostCount = 1u << hostBits;
        // Skip network (0), reserved gateway (.1 when /24-style), and broadcast (last).
        var start = hostCount > 2 ? 2u : 1u;
        var endExclusive = hostCount > 1 ? hostCount - 1 : hostCount;

        for (var host = start; host < endExclusive; host++)
        {
            var addr = netUint + host;
            var bytes = BitConverter.GetBytes(addr);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            yield return new IPAddress(bytes);
        }
    }

    private static string StripPort(string host)
    {
        var colon = host.LastIndexOf(':');
        if (colon > 0
            && host.IndexOf('.') >= 0
            && int.TryParse(host[(colon + 1)..], out var port)
            && port is >= 0 and <= 65535)
        {
            return host[..colon];
        }

        return host;
    }
}
