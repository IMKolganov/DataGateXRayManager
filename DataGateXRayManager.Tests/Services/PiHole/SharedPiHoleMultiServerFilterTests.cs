using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Tests.Services.PiHole;

/// <summary>
/// Shared Pi-hole on a host that also runs many OpenVPN + Xray nodes:
/// each Xray collector must only see its own include prefix (and optional excludes).
/// </summary>
public class SharedPiHoleMultiServerFilterTests
{
    private static readonly string[] OpenVpnPrefixes =
    [
        "10.51.11.",
        "10.51.12.",
        "10.51.13.",
        "10.51.14.",
        "10.51.15."
    ];

    private static readonly string[] XrayPrefixes =
    [
        "10.80.1.",
        "10.80.2.",
        "10.80.3.",
        "10.80.4.",
        "10.80.5."
    ];

    [Fact]
    public void SharedDump_EmptyPrefix_IngestsNothing_ForEveryXrayNode()
    {
        var dump = BuildSharedDump();

        foreach (var _ in XrayPrefixes)
        {
            var options = new PiHoleOptions { ClientSubnetPrefix = "" };
            var filtered = PiHoleApiClient.FilterForXrayNode(dump, options);
            Assert.Empty(filtered);
        }
    }

    [Fact]
    public void SharedDump_EachXrayPrefix_SeesOnlyOwnSubnet_NotOpenVpnOrSiblingXray()
    {
        var dump = BuildSharedDump();

        for (var i = 0; i < XrayPrefixes.Length; i++)
        {
            var prefix = XrayPrefixes[i];
            var options = new PiHoleOptions { ClientSubnetPrefix = prefix };
            var filtered = PiHoleApiClient.FilterForXrayNode(dump, options);

            Assert.Equal(2, filtered.Count); // two clients per xray prefix in dump
            Assert.All(filtered, r => Assert.StartsWith(prefix, r.ClientIp, StringComparison.Ordinal));
            Assert.DoesNotContain(filtered, r => OpenVpnPrefixes.Any(ov => r.ClientIp.StartsWith(ov, StringComparison.Ordinal)));
            Assert.DoesNotContain(
                filtered,
                r => XrayPrefixes.Where((_, j) => j != i).Any(other => r.ClientIp.StartsWith(other, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void SharedDump_ExcludeOpenVpnPrefixes_WithBroadInclude_StillDropsOpenVpn()
    {
        var dump = BuildSharedDump();
        // Misconfigured broad include that would otherwise swallow OpenVPN — excludes must win after include.
        var options = new PiHoleOptions
        {
            ClientSubnetPrefix = "10.",
            ClientSubnetExcludePrefixes = string.Join(",", OpenVpnPrefixes)
        };

        var filtered = PiHoleApiClient.FilterForXrayNode(dump, options);

        Assert.All(filtered, r => Assert.StartsWith("10.80.", r.ClientIp, StringComparison.Ordinal));
        Assert.DoesNotContain(filtered, r => r.ClientIp.StartsWith("10.51.", StringComparison.Ordinal));
        Assert.Equal(XrayPrefixes.Length * 2, filtered.Count);
    }

    [Fact]
    public void SharedDump_OpenVpnStyleInclude_DoesNotReturnXrayClients()
    {
        var dump = BuildSharedDump();

        foreach (var ovpn in OpenVpnPrefixes)
        {
            // OpenVPN collectors use include-only (requirePrefix false / classic Apply).
            var filtered = PiHoleSubnetFilter.Apply(dump, ovpn, requirePrefix: false);
            Assert.Equal(2, filtered.Count);
            Assert.All(filtered, r => Assert.StartsWith(ovpn, r.ClientIp, StringComparison.Ordinal));
            Assert.DoesNotContain(filtered, r => r.ClientIp.StartsWith("10.80.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Matching_UserCn_IsOrthogonalToSubnetFilter_DocumentedContract()
    {
        // Subnet filter only scopes IPs. CN matching happens later (dashboard SaveBatch / future enricher).
        var dump = new[]
        {
            new PiHoleQueryRecord(1, "10.80.1.5", "a.example", "A", "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "10.51.15.7", "b.example", "A", "FORWARDED", DateTimeOffset.UtcNow),
        };

        var scoped = PiHoleApiClient.FilterForXrayNode(
            dump,
            new PiHoleOptions { ClientSubnetPrefix = "10.80.1." });

        Assert.Single(scoped);
        Assert.Equal("10.80.1.5", scoped[0].ClientIp);
        Assert.Equal("a.example", scoped[0].Domain);
        // PiHoleQueryRecord has no CommonName — CN mapping is not done at filter stage.
    }

    private static List<PiHoleQueryRecord> BuildSharedDump()
    {
        var records = new List<PiHoleQueryRecord>();
        long id = 1;
        foreach (var prefix in OpenVpnPrefixes)
        {
            records.Add(new PiHoleQueryRecord(id++, $"{prefix}10", $"ovpn-a-{prefix}", "A", "FORWARDED", DateTimeOffset.UtcNow));
            records.Add(new PiHoleQueryRecord(id++, $"{prefix}11", $"ovpn-b-{prefix}", "A", "CACHE", DateTimeOffset.UtcNow));
        }

        foreach (var prefix in XrayPrefixes)
        {
            records.Add(new PiHoleQueryRecord(id++, $"{prefix}10", $"xray-a-{prefix}", "A", "FORWARDED", DateTimeOffset.UtcNow));
            records.Add(new PiHoleQueryRecord(id++, $"{prefix}11", $"xray-b-{prefix}", "A", "FORWARDED", DateTimeOffset.UtcNow));
        }

        // Noise: docker / host
        records.Add(new PiHoleQueryRecord(id++, "172.20.0.2", "nginx.local", "A", "FORWARDED", DateTimeOffset.UtcNow));
        records.Add(new PiHoleQueryRecord(id, "127.0.0.1", "localhost", "A", "CACHE", DateTimeOffset.UtcNow));
        return records;
    }
}
