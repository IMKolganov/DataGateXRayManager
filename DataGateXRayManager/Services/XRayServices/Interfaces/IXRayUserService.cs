using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>Same responsibilities as EasyRSA in OpenVPN manager: enroll/revoke/list "certificates" (here: VLESS clients).</summary>
public interface IXRayUserService
{
    Task<ServerCertificate> BuildCertificateAsync(string dataDir, CancellationToken cancellationToken,
        string commonName = "client1", int certExpireDays = 365);

    Task<ServerCertificate> RevokeCertificateAsync(string dataDir, string commonName,
        CancellationToken cancellationToken);

    Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string dataDir,
        CancellationToken cancellationToken);

    /// <summary>Removes the user from the live inbound via <c>xray api rmu</c> (connections may drop; user can be re-added with <c>adu</c>).</summary>
    Task KickInboundUserAsync(string commonName, CancellationToken cancellationToken);
}
