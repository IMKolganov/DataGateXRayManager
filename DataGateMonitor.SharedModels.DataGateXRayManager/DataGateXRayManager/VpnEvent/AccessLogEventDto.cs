namespace DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent;

/// <summary>Structured XRay access.log line (JSON). Fields depend on Xray-core version and inbound type.</summary>
public sealed class AccessLogEventDto
{
    public DateTimeOffset? TimeUtc { get; set; }

    /// <summary>VLESS email / account label (same idea as OpenVPN CommonName).</summary>
    public string? UserEmail { get; set; }

    public string? InboundTag { get; set; }
    public string? OutboundTag { get; set; }

    public string? ClientIp { get; set; }
    public int? ClientPort { get; set; }

    public string? Network { get; set; }
    public string? Destination { get; set; }

    /// <summary>When present in log (not always).</summary>
    public long? BytesUp { get; set; }

    public long? BytesDown { get; set; }

    /// <summary>Original JSON line for fields we do not map yet.</summary>
    public string? RawJson { get; set; }
}
