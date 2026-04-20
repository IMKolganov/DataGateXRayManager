namespace DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Requests;

public class GenerateClientLinkRequest
{
    public string CommonName { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string ConfigTemplate { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string IssuedTo { get; set; } = "xrayClient";
    public int LinkExpireDays { get; set; } = 365;
}
