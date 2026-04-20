using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

/// <summary>
/// Dashboard backend polls <c>GET {ApiUrl}/api/xray/clients</c> (see DataGateMonitor <c>XrayNodeApiClient</c>).
/// Returns active sessions; currently empty until wired to Xray stats / inbounds.
/// </summary>
[ApiController]
[Route("api/xray")]
public sealed class XrayClientsController : ControllerBase
{
    [HttpGet("clients")]
    public ActionResult<XrayClientsPayload> GetActiveClients() =>
        Ok(new XrayClientsPayload { Clients = [] });
}

public sealed class XrayClientsPayload
{
    public List<XrayClientSessionDto> Clients { get; set; } = [];
}

public sealed class XrayClientSessionDto
{
    public string Email { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public string? Username { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public DateTimeOffset ConnectedSince { get; set; }
}
