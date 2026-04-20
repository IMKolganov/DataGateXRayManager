namespace DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

public enum CertificateStatus
{
    Valid = 0,
    Revoked = 1,
    Expired = 2,
    Unknown = 99
}
