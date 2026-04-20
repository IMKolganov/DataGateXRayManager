namespace DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;

public class ClientLinkMetadata
{
    public string CommonName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string IssuedTo { get; set; } = string.Empty;
    public string? CertFilePath { get; set; }
    public string? KeyFilePath { get; set; }
}
