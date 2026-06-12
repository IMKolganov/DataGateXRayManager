namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Resolved <c>xray api statsonlineiplist</c> capability for the running Xray-core binary.
/// </summary>
public enum XRayStatOnlineIpListMode
{
    /// <summary><c>statsonlineiplist -all -include-traffic</c> is supported.</summary>
    AllWithTraffic,

    /// <summary><c>-all</c> works but <c>-include-traffic</c> does not.</summary>
    AllWithoutTraffic,

    /// <summary>Only <c>-email</c> is supported; bulk polling uses legacy per-user calls.</summary>
    LegacyPerEmail
}
