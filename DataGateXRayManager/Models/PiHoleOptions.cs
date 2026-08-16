namespace DataGateXRayManager.Models;

public sealed class PiHoleOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    public string AppPassword { get; set; } = string.Empty;

    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 200;

    public int LookbackSeconds { get; set; } = 120;

    /// <summary>Only collect queries from client IPs starting with this prefix (e.g. 10.80.0.). Required on shared Pi-hole.</summary>
    public string ClientSubnetPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated client IP prefixes to drop (e.g. <c>10.51.15.,10.51.16.</c> for co-located OpenVPN).
    /// Applied after <see cref="ClientSubnetPrefix"/>.
    /// </summary>
    public string ClientSubnetExcludePrefixes { get; set; } = string.Empty;
}
