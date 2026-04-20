using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Enums;

namespace DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;

public class ActiveProxyConnection
{
    public string ConnectionId { get; set; } = string.Empty;
    public ProxyConnectionProtocol Protocol { get; set; }
    public string? RealClientIp { get; set; }
    public int RealClientPort { get; set; }
    public string? LocalProxyIp { get; set; }
    public int LocalProxyPort { get; set; }
    public string? TargetIp { get; set; }
    public int TargetPort { get; set; }
    public DateTime ConnectedAtUtc { get; set; }
}
