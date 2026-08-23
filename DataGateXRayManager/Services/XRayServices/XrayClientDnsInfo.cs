using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Builds client-facing VPN DNS hints from node env (<c>DNS1</c>/<c>DNS2</c>, <c>XRAY_DNS_IDENTITY_*</c>).
/// VLESS does not push DNS; clients must apply <see cref="BuildClientDnsServers"/> via the OS tunnel.
/// </summary>
public static class XrayClientDnsInfo
{
    public static bool IsDnsIdentityEnabled(IConfiguration configuration) =>
        IsTruthy(configuration["XRAY_DNS_IDENTITY_ENABLED"]
                 ?? configuration["Xray:DnsIdentity:Enabled"]);

    public static List<string> BuildClientDnsServers(string? dns1, string? dns2)
    {
        var list = new List<string>(2);
        AddIfPresent(list, dns1);
        AddIfPresent(list, dns2);
        return list;
    }

    public static List<string> BuildClientDnsServers(IConfiguration configuration) =>
        BuildClientDnsServers(configuration["DNS1"], configuration["DNS2"]);

    /// <summary>Raw JSON array for link templates, e.g. <c>["172.20.0.1"]</c>.</summary>
    public static string ToDnsServersJson(IReadOnlyList<string> servers) =>
        JsonSerializer.Serialize(servers);

    public static bool IsTruthy(string? value) =>
        value is not null
        && value.Trim() is "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON";

    private static void AddIfPresent(List<string> list, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;
        if (list.Exists(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(trimmed);
    }
}
