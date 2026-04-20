namespace DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Requests;

public class RevokeServerCertificateRequest
{
    public string CommonName { get; set; } = string.Empty;
}
