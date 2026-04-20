namespace DataGateXRayManager.Services.XRayServices;

public class StoredXRayClient
{
    public string CommonName { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public bool IsRevoked { get; set; }
    public string Flow { get; set; } = "";
}
