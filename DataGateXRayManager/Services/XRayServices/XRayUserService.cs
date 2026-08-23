using DataGateXRayManager.Helpers;
using Newtonsoft.Json.Linq;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

namespace DataGateXRayManager.Services.XRayServices;

public class XRayUserService(
    IConfiguration configuration,
    IDataPathResolver dataPathResolver,
    IXRayProcessApiRunner xrayApi,
    IXrayClientStore clientStore,
    IXrayClientStoreLock storeLock,
    IXrayDnsIdentitySyncService dnsIdentitySync,
    ILogger<XRayUserService> logger) : IXRayUserService
{
    private string InboundTag => configuration["XRay:InboundTag"] ?? "vless-in";

    private string DefaultFlow => configuration["XRay:DefaultClientFlow"] ?? "";

    private string IdentitySubnet => configuration["XRAY_DNS_IDENTITY_SUBNET"]
                                     ?? configuration["Xray:DnsIdentity:Subnet"]
                                     ?? XrayDnsIdentityAllocator.DefaultSubnetCidr;

    public async Task KickInboundUserAsync(string commonName, CancellationToken cancellationToken)
    {
        await xrayApi.RunApiVerbAsync(["rmu", $"-tag={InboundTag}", commonName], null, cancellationToken, XRayApiCallOptions.Default);

        // rmu drops the user from the running inbound only; clients.store.json still lists them. Without a
        // follow-up adu, reconnects fail (unknown UUID). Re-push active store rows so "kick" = drop session, keep credential.
        var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
        var store = await clientStore.LoadAsync(dataDir, cancellationToken);
        var client = store.FirstOrDefault(c =>
            !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase));
        if (client is null)
            return;

        var userJson = BuildAddUserJson(client.CommonName, client.Uuid, client.Flow ?? "");
        await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken, XRayApiCallOptions.Default);
        logger.LogInformation("Kick: re-added {CommonName} to running Xray after rmu.", commonName);
    }

    public async Task<int> RehydrateRunningXrayFromStoreAsync(string dataDir, CancellationToken cancellationToken)
    {
        dataDir = Path.GetFullPath(dataDir);
        var store = await clientStore.LoadAsync(dataDir, cancellationToken);
        return await RehydrateClientsAsync(store.Where(c => !c.IsRevoked).ToList(), cancellationToken);
    }

    public async Task<int> RehydrateClientsAsync(IReadOnlyList<StoredXRayClient> clients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (clients.Count == 0)
        {
            logger.LogInformation("Xray store rehydrate: no active clients to push.");
            return 0;
        }

        logger.LogInformation("Xray store rehydrate: pushing {Count} client(s) to running Xray via adu.", clients.Count);
        var ok = 0;
        foreach (var c in clients)
        {
            if (c.IsRevoked)
                continue;
            try
            {
                var userJson = BuildAddUserJson(c.CommonName, c.Uuid, c.Flow ?? "");
                await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken, XRayApiCallOptions.Default);
                logger.LogInformation("Rehydrated Xray VLESS client {CommonName} (UUID={Uuid}).", c.CommonName, c.Uuid);
                ok++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Rehydrate failed for {CommonName} (UUID={Uuid}); client may already exist or Xray API error.",
                    c.CommonName, c.Uuid);
            }
        }

        return ok;
    }

    public async Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string dataDir,
        CancellationToken cancellationToken)
    {
        var list = await clientStore.LoadAsync(dataDir, cancellationToken);
        return list.Where(c => !c.IsRevoked).Select(MapToServerCertificate).ToList();
    }

    public async Task<ServerCertificate> BuildCertificateAsync(string dataDir, CancellationToken cancellationToken,
        string commonName = "client1", int certExpireDays = 365)
    {
        dataDir = Path.GetFullPath(dataDir);
        StoredXRayClient client;
        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await clientStore.LoadUnlockedAsync(dataDir, cancellationToken).ConfigureAwait(false);
            if (store.Any(c => !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Client with CommonName '{commonName}' already exists.");

            var uuid = Guid.NewGuid().ToString();
            var flow = DefaultFlow;
            client = new StoredXRayClient
            {
                CommonName = commonName,
                Uuid = uuid,
                CreatedUtc = DateTime.UtcNow,
                Flow = flow
            };

            if (dnsIdentitySync.IsEnabled)
            {
                var used = store.Where(c => !c.IsRevoked).Select(c => c.IdentityIp);
                client.IdentityIp = XrayDnsIdentityAllocator.AllocateNext(IdentitySubnet, used)
                                    ?? throw new InvalidOperationException(
                                        $"DNS identity IP pool exhausted for subnet '{IdentitySubnet}'.");
            }

            store.Add(client);
            await clientStore.SaveUnlockedAsync(dataDir, store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            storeLock.Release();
        }

        if (dnsIdentitySync.IsEnabled)
        {
            try
            {
                await dnsIdentitySync.SyncAsync(cancellationToken);
            }
            catch
            {
                await RollbackNewClientAsync(dataDir, client, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            var userJson = BuildAddUserJson(client.CommonName, client.Uuid, client.Flow ?? "");
            await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken, XRayApiCallOptions.Default);
        }

        logger.LogInformation(
            "XRay VLESS client created: CN={CommonName}, UUID={Uuid}, IdentityIp={IdentityIp}",
            client.CommonName, client.Uuid, client.IdentityIp ?? "(none)");
        return MapToServerCertificate(client);
    }

    private async Task RollbackNewClientAsync(string dataDir, StoredXRayClient client, CancellationToken cancellationToken)
    {
        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await clientStore.LoadUnlockedAsync(dataDir, cancellationToken).ConfigureAwait(false);
            var removed = store.RemoveAll(c =>
                string.Equals(c.Uuid, client.Uuid, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await clientStore.SaveUnlockedAsync(dataDir, store, cancellationToken).ConfigureAwait(false);
                logger.LogWarning(
                    "Rolled back Xray client {CommonName} (UUID={Uuid}) after DNS identity sync failure.",
                    client.CommonName,
                    client.Uuid);
            }
        }
        finally
        {
            storeLock.Release();
        }
    }

    public async Task<ServerCertificate> RevokeCertificateAsync(string dataDir, string commonName,
        CancellationToken cancellationToken)
    {
        dataDir = Path.GetFullPath(dataDir);
        StoredXRayClient client;
        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await clientStore.LoadUnlockedAsync(dataDir, cancellationToken).ConfigureAwait(false);
            var found = store.FirstOrDefault(c =>
                !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase));
            if (found is null)
                throw new InvalidOperationException($"Client '{commonName}' not found.");

            client = found;
            await xrayApi.RunApiVerbAsync(["rmu", $"-tag={InboundTag}", commonName], null, cancellationToken, XRayApiCallOptions.Default);

            client.IsRevoked = true;
            client.RevokedUtc = DateTime.UtcNow;
            client.IdentityIp = null;
            await clientStore.SaveUnlockedAsync(dataDir, store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            storeLock.Release();
        }

        if (dnsIdentitySync.IsEnabled)
            await dnsIdentitySync.SyncAsync(cancellationToken);

        return new ServerCertificate
        {
            CommonName = commonName,
            IsRevoked = true,
            Status = CertificateStatus.Revoked,
            SerialNumber = client.Uuid,
            Message = "Revoked",
            RevokeDate = client.RevokedUtc ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// JSON for <c>xray api adu stdin:</c>: must deserialize to an Xray config root with <c>inbounds</c>
    /// (see xray-core <c>extractInboundsConfig</c> / <c>inbound_user_add.go</c>). Each new VLESS client must have non-empty <c>email</c>.
    /// </summary>
    private string BuildAddUserJson(string email, string uuid, string flow)
    {
        var template = configuration["XRay:AduUserJsonTemplate"];
        if (!string.IsNullOrWhiteSpace(template))
        {
            return template
                .Replace("{{inboundTag}}", InboundTag, StringComparison.Ordinal)
                .Replace("{{email}}", email, StringComparison.Ordinal)
                .Replace("{{uuid}}", uuid, StringComparison.Ordinal)
                .Replace("{{flow}}", flow ?? "", StringComparison.Ordinal);
        }

        var client = new JObject
        {
            ["id"] = uuid,
            ["email"] = email,
            ["level"] = 0
        };
        if (!string.IsNullOrWhiteSpace(flow))
            client["flow"] = flow;

        var inbound = new JObject
        {
            ["tag"] = InboundTag,
            ["listen"] = "0.0.0.0",
            ["port"] = 1,
            ["protocol"] = "vless",
            ["settings"] = new JObject
            {
                ["decryption"] = "none",
                ["clients"] = new JArray { client }
            }
        };

        var root = new JObject { ["inbounds"] = new JArray { inbound } };
        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static ServerCertificate MapToServerCertificate(StoredXRayClient c) =>
        new()
        {
            CommonName = c.CommonName,
            SerialNumber = c.Uuid,
            Status = c.IsRevoked ? CertificateStatus.Revoked : CertificateStatus.Valid,
            IsRevoked = c.IsRevoked,
            ExpiryDate = c.CreatedUtc,
            CertificatePath = Path.Combine("xray", "clients", $"{c.CommonName}.json"),
            KeyPath = null,
            Message = "VLESS client",
            IdentityIp = c.IdentityIp
        };
}
