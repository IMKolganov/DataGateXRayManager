namespace DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent.Requests;

public class VpnEventRequest
{
    public string? CommonName { get; set; }
    public string? RealAddress { get; set; }
    public long? DurationSec { get; set; }
    public string? VirtualAddress { get; set; }
    public string? Message { get; set; }
    public string? EventType { get; set; }

    /// <summary>Optional: client app name/version when your hook sends it (XRay itself does not provide this).</summary>
    public string? ClientSoftwareVersion { get; set; }

    public string? InboundTag { get; set; }
    public long? BytesUp { get; set; }
    public long? BytesDown { get; set; }

    /// <summary>Arbitrary JSON or text from external integrations.</summary>
    public string? Metadata { get; set; }
}
