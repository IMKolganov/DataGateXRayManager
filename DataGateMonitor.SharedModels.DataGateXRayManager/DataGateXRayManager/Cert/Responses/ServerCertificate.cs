namespace DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

public class ServerCertificate
{
    public CertificateStatus Status { get; set; }
    public DateTime ExpiryDate { get; set; }
    public DateTime RevokeDate { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string UnknownField { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public string? CertificatePath { get; set; }
    public string? KeyPath { get; set; }
    public string? Message { get; set; }
}
