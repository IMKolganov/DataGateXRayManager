using DataGateXRayManager.Services.Interfaces;
using DataGateXRayManager.Services.XRayServices;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;

namespace DataGateXRayManager.Services;

public class ClientLinkService(ILogger<ClientLinkService> logger, IXRayUserService xRayUserService)
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

        logger.LogInformation("Creating XRay client + link file for {CommonName}", commonName);
        var certResult = await xRayUserService.BuildCertificateAsync(dataDir, cancellationToken, commonName, linkExpireDays);

        var linksDir = Path.Combine(dataDir, "xray", "links");
        Directory.CreateDirectory(linksDir);

        var vlessUri = BuildVlessUriPlaceholder(configTemplate, certResult, serverIp, serverPort);
        var content = GenerateLinkFile(configTemplate, friendlyName, serverIp, serverPort, certResult, vlessUri);

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

    private static string BuildVlessUriPlaceholder(string template, ServerCertificate cert,
        string serverIp, int serverPort)
    {
        if (!template.Contains("{{vless_uri}}", StringComparison.Ordinal))
            return "";
        return
            $"vless://{cert.SerialNumber}@{serverIp}:{serverPort}?encryption=none&type=tcp#client";
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
