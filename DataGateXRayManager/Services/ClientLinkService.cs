using System.Net;
using DataGateXRayManager.Services.Interfaces;
using DataGateXRayManager.Services.XRayServices;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;
using Microsoft.Extensions.Configuration;

namespace DataGateXRayManager.Services;

public class ClientLinkService(ILogger<ClientLinkService> logger, IXRayUserService xRayUserService, IConfiguration configuration)
    : IClientLinkService
{
    public async Task<ClientLinkMetadata> AddClientLink(string dataDir, string commonName, string friendlyName,
        string configTemplate,
        string serverIp, int serverPort, CancellationToken cancellationToken,
        string issuedTo = "xrayClient", int linkExpireDays = 365)
    {
        dataDir = Path.GetFullPath(dataDir);
        if (string.IsNullOrEmpty(commonName) || string.IsNullOrEmpty(configTemplate))
            throw new ArgumentException("Common name and config template are required");

        var (host, port) = NormalizeServerEndpoint(serverIp, serverPort);
        if (host != serverIp || port != serverPort)
            logger.LogWarning(
                "Normalized Xray client endpoint (avoid host:port + separate port). Before {BeforeIp}:{BeforePort}, after {AfterIp}:{AfterPort}.",
                serverIp, serverPort, host, port);

        logger.LogInformation("Creating XRay client + link file for {CommonName}", commonName);
        var certResult = await xRayUserService.BuildCertificateAsync(dataDir, cancellationToken, commonName, linkExpireDays);

        var linksDir = Path.Combine(dataDir, "xray", "links");
        Directory.CreateDirectory(linksDir);

        var vlessUri = BuildVlessUriPlaceholder(configTemplate, certResult, host, port, friendlyName);
        var content = GenerateLinkFile(configTemplate, friendlyName, host, port, certResult, vlessUri);

        var ext = Path.GetExtension(configTemplate);
        if (string.IsNullOrEmpty(ext) || ext.Length > 8)
            ext = ".txt";

        var fileName = $"{commonName}{ext}";
        var fullPath = Path.Combine(linksDir, fileName);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);

        var fileInfo = new FileInfo(fullPath);
        return new ClientLinkMetadata
        {
            CommonName = commonName,
            FileName = fileInfo.Name,
            FilePath = fileInfo.FullName,
            IssuedAt = DateTime.UtcNow,
            IssuedTo = issuedTo,
            CertFilePath = certResult.CertificatePath,
            KeyFilePath = certResult.KeyPath
        };
    }

    public async Task<ClientLinkMetadata?> RevokeClientLink(string dataDir, string commonName, string fileName,
        string filePath, CancellationToken cancellationToken)
    {
        dataDir = Path.GetFullPath(dataDir);
        var serverCertificate = await xRayUserService.RevokeCertificateAsync(dataDir, commonName, cancellationToken);
        logger.LogInformation("Revoke client result: {Message} for {CommonName}", serverCertificate.Message, commonName);

        var revokedFilePath = MoveRevokedLink(fileName, filePath, dataDir);
        logger.LogInformation("Moved link file to {RevokedFilePath}", revokedFilePath);

        return new ClientLinkMetadata
        {
            CommonName = commonName,
            FileName = fileName,
            FilePath = revokedFilePath,
            CertFilePath = serverCertificate.CertificatePath,
            KeyFilePath = serverCertificate.KeyPath
        };
    }

    public async Task<ClientLinkDownload> DownloadClientLink(string fileName, string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File {filePath} does not exist");

        var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return new ClientLinkDownload { FileName = fileName, Content = content };
    }

    /// <summary>
    /// If <paramref name="serverIp"/> already contains <c>host:port</c> (e.g. DB mistake:
    /// <c>dev-x1.example.com:30443</c> with <c>VpnServerPort</c> still 443), strip the inline port and use it so
    /// URIs do not become <c>…@host:30443:443</c>.
    /// </summary>
    private static (string Host, int Port) NormalizeServerEndpoint(string serverIp, int serverPort)
    {
        serverIp = (serverIp ?? "").Trim();
        if (serverIp.Length == 0)
            return (serverIp, serverPort);

        if (serverIp[0] == '[')
        {
            var end = serverIp.IndexOf(']', 1);
            if (end > 1 && end < serverIp.Length - 2 && serverIp[end + 1] == ':'
                && int.TryParse(serverIp.AsSpan(end + 2), out var p6) && p6 is > 0 and <= 65535)
                return (serverIp[..(end + 1)], p6);
            return (serverIp, serverPort);
        }

        if (serverIp.Count(c => c == ':') == 1)
        {
            var idx = serverIp.IndexOf(':');
            if (idx > 0 && int.TryParse(serverIp.AsSpan(idx + 1), out var p) && p is > 0 and <= 65535)
                return (serverIp[..idx], p);
        }

        return (serverIp, serverPort);
    }

    private string BuildVlessUriPlaceholder(string template, ServerCertificate cert,
        string serverIp, int serverPort, string friendlyName)
    {
        if (!template.Contains("{{vless_uri}}", StringComparison.Ordinal))
            return "";
        var normalizedFriendlyName = (friendlyName ?? string.Empty).Trim();
        var serverNameOnly = normalizedFriendlyName.Split('[')[0].Trim();
        var labelBase = string.IsNullOrWhiteSpace(serverNameOnly) ? "DataGate" : $"DataGate {serverNameOnly}";
        var label = labelBase.Replace(" ", "+", StringComparison.Ordinal);
        var uuid = cert.SerialNumber;
        var transportMode = ResolveTransportMode();
        return transportMode switch
        {
            "tls" => BuildVlessTlsUri(uuid, serverIp, serverPort, label),
            "reality" => BuildVlessRealityUriPlaceholder(uuid, serverIp, serverPort, label),
            _ => $"vless://{uuid}@{serverIp}:{serverPort}?encryption=none&type=tcp#{label}"
        };
    }

    /// <summary>Matches <c>XRAY_TRANSPORT_MODE</c> in <c>entrypoint.sh</c> / Docker (plain | tls | reality).</summary>
    private string ResolveTransportMode()
    {
        var raw = configuration["XRAY_TRANSPORT_MODE"]
                  ?? Environment.GetEnvironmentVariable("XRAY_TRANSPORT_MODE")
                  ?? "plain";
        raw = raw.Trim().ToLowerInvariant();
        return raw switch
        {
            "tcp" or "none" => "plain",
            _ => raw
        };
    }

    /// <summary>Public hostname for TLS SNI when <paramref name="serverHost"/> is an IP (e.g. from DB). Docker: <c>XRAY__DOMAIN</c>.</summary>
    private string? ResolveTlsSniHost(string serverHost)
    {
        var fromConfig = configuration["XRAY:DOMAIN"]
                         ?? configuration["XRay:Domain"]
                         ?? Environment.GetEnvironmentVariable("XRAY__DOMAIN");
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig.Trim();

        var host = (serverHost ?? "").Trim();
        if (host.Length == 0 || IsIpLiteral(host))
            return null;
        // Strip brackets for IPv6 display host — if not IP, use as SNI
        return host.TrimStart('[').Split(']')[0];
    }

    private string BuildVlessTlsUri(string uuid, string serverHost, int serverPort, string fragmentLabel)
    {
        var sniFromConfig = ResolveTlsSniHost(serverHost);
        var sni = sniFromConfig ?? serverHost.Trim();
        if (IsIpLiteral(serverHost) && sniFromConfig == null)
        {
            logger.LogWarning(
                "XRAY_TRANSPORT_MODE=tls but server endpoint is IP {Host} and no XRAY__DOMAIN / XRAY:Domain is set; using IP as SNI (certificate validation may fail on clients).",
                serverHost);
        }

        var query =
            $"encryption=none&security=tls&sni={Uri.EscapeDataString(sni)}&type=tcp";
        return $"vless://{uuid}@{serverHost}:{serverPort}?{query}#{fragmentLabel}";
    }

    private string BuildVlessRealityUriPlaceholder(string uuid, string serverHost, int serverPort, string fragmentLabel)
    {
        logger.LogWarning(
            "XRAY_TRANSPORT_MODE=reality: share URI is not auto-generated with Reality params (pbk/sid); clients need a matching Reality profile. Falling back to plain-style URI (likely invalid for Reality inbound).");
        return $"vless://{uuid}@{serverHost}:{serverPort}?encryption=none&type=tcp#{fragmentLabel}";
    }

    private static bool IsIpLiteral(string host)
    {
        var h = host.Trim();
        if (h.Length >= 2 && h[0] == '[')
        {
            var end = h.IndexOf(']', 1);
            if (end > 1)
                h = h[1..end];
        }

        return IPAddress.TryParse(h, out _);
    }

    private static string GenerateLinkFile(
        string configTemplate,
        string friendlyName,
        string serverIp,
        int serverPort,
        ServerCertificate cert,
        string vlessUri)
    {
        return configTemplate
            .Replace("{{friendly_name}}", friendlyName, StringComparison.Ordinal)
            .Replace("{{server_ip}}", serverIp, StringComparison.Ordinal)
            .Replace("{{server_port}}", serverPort.ToString(), StringComparison.Ordinal)
            .Replace("{{uuid}}", cert.SerialNumber, StringComparison.Ordinal)
            .Replace("{{vless_uri}}", vlessUri, StringComparison.Ordinal);
    }

    private static string MoveRevokedLink(string linkFileName, string linkFilePath, string dataDir)
    {
        var revokedDir = Path.Combine(dataDir, "xray", "revoked", "links");
        Directory.CreateDirectory(revokedDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueFileName = $"{Path.GetFileNameWithoutExtension(linkFileName)}_{timestamp}{Path.GetExtension(linkFileName)}";
        var revokedPath = Path.Combine(revokedDir, uniqueFileName);

        if (File.Exists(linkFilePath))
            File.Move(linkFilePath, revokedPath);
        return revokedPath;
    }
}
