namespace DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

/// <summary>One logical online session for the dashboard (aligned with the monitor node DTO field names).</summary>
public sealed class XrayClientSessionDto
{
    public string Email { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public string? Username { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public DateTimeOffset ConnectedSince { get; set; }
}

/// <summary>Payload for <c>GET /api/xray/clients</c>.</summary>
public sealed class XrayClientsEnvelope
{
    public List<XrayClientSessionDto> Clients { get; set; } = [];
    public string? PollError { get; set; }
    public DateTimeOffset PolledAt { get; set; }
}

public sealed class XrayCommonNameRequest
{
    public string CommonName { get; set; } = string.Empty;
}
