namespace DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Requests;

public class GetProxyClientByLocalPortRequest
{
    public int LocalPort { get; set; }
    public string? Host { get; set; }
}
