using DataGateMonitor.SharedModels.ClientIdentity;

namespace DataGateXRayManager.Helpers;

/// <summary>
/// The backend rejects requests without X-Client-Name / X-Client-Version; every backend HttpClient must call <see cref="Apply"/>.
/// </summary>
internal static class BackendClientIdentity
{
    public static readonly string Version =
        typeof(BackendClientIdentity).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static void Apply(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(ClientIdentityRules.NameHeader);
        client.DefaultRequestHeaders.Remove(ClientIdentityRules.VersionHeader);
        client.DefaultRequestHeaders.Add(ClientIdentityRules.NameHeader, ClientNames.XrayNode);
        client.DefaultRequestHeaders.Add(ClientIdentityRules.VersionHeader, Version);
    }
}
