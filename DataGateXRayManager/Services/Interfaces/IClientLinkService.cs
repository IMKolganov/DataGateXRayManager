using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;

namespace DataGateXRayManager.Services.Interfaces;

public interface IClientLinkService
{
    Task<ClientLinkMetadata> AddClientLink(string dataDir, string commonName, string friendlyName, string configTemplate,
        string serverIp, int serverPort, CancellationToken cancellationToken,
        string issuedTo = "xrayClient", int linkExpireDays = 365);

    Task<ClientLinkMetadata?> RevokeClientLink(string dataDir, string commonName,
        string fileName, string filePath, CancellationToken cancellationToken);

    Task<ClientLinkDownload> DownloadClientLink(string fileName, string filePath, CancellationToken cancellationToken);
}
