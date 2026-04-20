namespace DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Requests;

public class RevokeClientLinkRequest
{
    public string CommonName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
