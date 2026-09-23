namespace DataGateXRayManager.Helpers;

/// <summary>
/// Resolves the public manager ApiUrl used when announcing this node to the dashboard.
/// </summary>
public static class VpnServerAnnounceApiUrlResolver
{
    public const string PublicApiUrlKey = "PUBLIC_API_URL";
    public const int DefaultApiPort = 5010;

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

    public static string? GetConfiguredPublicApiUrl(IConfiguration configuration)
    {
        var value = Environment.GetEnvironmentVariable(PublicApiUrlKey)
            ?? configuration[PublicApiUrlKey];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Xray TLS / announce hostname from <c>XRAY__DOMAIN</c> or <c>XRAY_DOMAIN</c>.
    /// </summary>
    public static string? GetConfiguredDomain(IConfiguration configuration)
    {
        var value = Environment.GetEnvironmentVariable("XRAY__DOMAIN")
            ?? Environment.GetEnvironmentVariable("XRAY_DOMAIN")
            ?? configuration["XRAY:DOMAIN"]
            ?? configuration["XRAY__DOMAIN"]
            ?? configuration["XRAY_DOMAIN"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static int ResolveApiPort(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("API_PORT")
            ?? configuration["API_PORT"];
        if (int.TryParse(raw, out var port) && port > 0)
            return port;
        return DefaultApiPort;
    }

    public static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
