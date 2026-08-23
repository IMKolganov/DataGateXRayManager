namespace DataGateXRayManager.Services.XRayServices;

public class StoredXRayClient
{
    public string CommonName { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public bool IsRevoked { get; set; }
    public string Flow { get; set; } = "";

    /// <summary>
    /// Stable source IP for DNS egress (sendThrough) so Pi-hole ClientIp maps to this CommonName.
    /// Pool from <c>XRAY_DNS_IDENTITY_SUBNET</c> (default 10.80.0.0/24).
    /// </summary>
    public string? IdentityIp { get; set; }
}
