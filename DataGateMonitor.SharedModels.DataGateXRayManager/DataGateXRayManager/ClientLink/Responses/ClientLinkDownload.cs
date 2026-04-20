namespace DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;

public class ClientLinkDownload
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
