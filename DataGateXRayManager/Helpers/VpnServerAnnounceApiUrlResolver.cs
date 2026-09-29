using System.Net;
using System.Net.Sockets;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DataGateXRayManager.Helpers;

/// <summary>
/// Resolves the public manager ApiUrl used when announcing this node to the dashboard.
/// </summary>
public static class VpnServerAnnounceApiUrlResolver
{
    public const string PublicApiUrlKey = "PUBLIC_API_URL";
    public const string PublicIpKey = "PUBLIC_IP";
    public const string XrayConfiguredIpKey = "XRAY:IP";
    public const string XrayConfiguredDomainKey = "XRAY:DOMAIN";
    public const string XrayApiHttpsPortKey = "XRAY_API_HTTPS_PORT";
    public const int DefaultApiPort = 5010;
    public const int DefaultXrayApiHttpsPort = 9443;
    private static readonly TimeSpan DomainDnsTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Prefer an explicit public URL; otherwise <c>https://{domain}:{apiPort}/</c>;
    /// otherwise <c>http://{publicIp}:{apiPort}/</c>.
    /// </summary>
    public static string? Resolve(
        string? publicApiUrl,
        string? domain,
        string? publicIp,
        int apiPort)
    {
        if (!string.IsNullOrWhiteSpace(publicApiUrl))
            return EnsureTrailingSlash(publicApiUrl.Trim());

        if (!string.IsNullOrWhiteSpace(domain) && apiPort > 0)
            return $"https://{domain.Trim().TrimEnd('/')}:{apiPort}/";

        if (string.IsNullOrWhiteSpace(publicIp) || apiPort <= 0)
            return null;

        return $"http://{publicIp.Trim()}:{apiPort}/";
    }

    /// <summary>Backward-compatible overload without domain (PUBLIC_API_URL → IP).</summary>
    public static string? Resolve(string? publicApiUrl, string? publicIp, int apiPort) =>
        Resolve(publicApiUrl, domain: null, publicIp, apiPort);

    /// <summary>
    /// Explicit <see cref="PublicApiUrlKey"/>, else HTTPS manager URL from install domain
    /// (<c>XRAY__DOMAIN</c> + <c>XRAY_API_HTTPS_PORT</c>, already on every Xray stack).
    /// </summary>
    public static string? GetConfiguredPublicApiUrl(IConfiguration configuration)
    {
        var explicitUrl = configuration[PublicApiUrlKey];
        if (!string.IsNullOrWhiteSpace(explicitUrl))
            return EnsureTrailingSlash(explicitUrl.Trim());

        return TryBuildPublicApiUrlFromXrayDomain(configuration);
    }

    /// <summary>
    /// Site <c>PUBLIC_IP</c> / compose <c>XRAY__IP</c> when set.
    /// Reads <see cref="IConfiguration"/> only (host builder already maps process env into config).
    /// </summary>
    public static string? GetConfiguredPublicIp(IConfiguration configuration)
    {
        foreach (var raw in new[]
                 {
                     configuration[PublicIpKey],
                     configuration[XrayConfiguredIpKey],
                     configuration["XRAY__IP"]
                 })
        {
            if (TryParseConfiguredPublicIp(raw, out var ip))
                return ip;
        }

        return null;
    }

    public static async Task<string?> ResolvePublicIpForAnnounceAsync(
        IConfiguration configuration,
        IExternalIpAddressService externalIpAddressService,
        CancellationToken cancellationToken)
    {
        var configured = GetConfiguredPublicIp(configuration);
        if (configured is not null)
            return configured;

        var domain = GetConfiguredDomain(configuration);
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var fromDns = await TryResolveDomainIpv4Async(domain, cancellationToken);
            if (fromDns is not null)
                return fromDns;
        }

        return await externalIpAddressService.GetPublicIpAddressAsync(cancellationToken);
    }

    internal static string? TryBuildPublicApiUrlFromXrayDomain(IConfiguration configuration)
    {
        var domain = GetConfiguredDomain(configuration);
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var port = ResolveXrayApiHttpsPort(configuration);
        return $"https://{domain}:{port}/";
    }

    /// <summary>
    /// Xray TLS / announce hostname from <c>XRAY__DOMAIN</c> or <c>XRAY_DOMAIN</c>.
    /// </summary>
    public static string? GetConfiguredDomain(IConfiguration configuration)
    {
        var value = Environment.GetEnvironmentVariable("XRAY__DOMAIN")
            ?? Environment.GetEnvironmentVariable("XRAY_DOMAIN")
            ?? configuration[XrayConfiguredDomainKey]
            ?? configuration["XRAY__DOMAIN"]
            ?? configuration["XRAY_DOMAIN"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Alias used by older call sites / tests.</summary>
    internal static string? GetConfiguredXrayDomain(IConfiguration configuration) =>
        GetConfiguredDomain(configuration);

    public static int ResolveXrayApiHttpsPort(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable(XrayApiHttpsPortKey)
            ?? configuration[XrayApiHttpsPortKey];
        if (int.TryParse(raw, out var port) && port is > 0 and <= 65535)
            return port;

        return DefaultXrayApiHttpsPort;
    }

    internal static async Task<string?> TryResolveDomainIpv4Async(string domain, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DomainDnsTimeout);
            var entries = await Dns.GetHostAddressesAsync(domain.Trim(), timeout.Token);
            var ipv4 = entries.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null)
                return null;

            return TryParseConfiguredPublicIp(ipv4.ToString(), out var ip) ? ip : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryParseConfiguredPublicIp(string? raw, out string ip)
    {
        ip = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var candidate = raw.Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        if (!IPAddress.TryParse(candidate, out var address))
            return false;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
            return false;

        ip = address.ToString();
        return true;
    }

    public static int ResolveApiPort(IConfiguration configuration)
    {
        var raw = configuration["API_PORT"];
        if (int.TryParse(raw, out var port) && port > 0)
            return port;
        return DefaultApiPort;
    }

    public static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
