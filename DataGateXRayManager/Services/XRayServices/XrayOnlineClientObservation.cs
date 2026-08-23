using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// One online user parsed from <c>statsonlineiplist</c> together with the freshest <c>lastSeen</c> across
/// their IPs. Xray-core only refreshes <c>lastSeen</c> when a new connection is opened, so it is the only
/// hint the API gives about an entry left behind by a connection that never terminated.
/// </summary>
public sealed record XrayOnlineClientObservation(XrayClientSessionDto Session, DateTimeOffset? LastSeenUtc);
