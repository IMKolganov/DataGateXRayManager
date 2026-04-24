using System.Text.Json;
using DataGateXRayManager.Helpers;
using Newtonsoft.Json.Linq;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

namespace DataGateXRayManager.Services.XRayServices;

public class XRayUserService(
    IConfiguration configuration,
    IDataPathResolver dataPathResolver,
    XRayProcessApi xrayApi,
    ILogger<XRayUserService> logger) : IXRayUserService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private string InboundTag => configuration["XRay:InboundTag"] ?? "vless-in";

    private string DefaultFlow => configuration["XRay:DefaultClientFlow"] ?? "";

    public async Task KickInboundUserAsync(string commonName, CancellationToken cancellationToken)
    {
        await xrayApi.RunApiVerbAsync(["rmu", $"-tag={InboundTag}", commonName], null, cancellationToken);

        // rmu drops the user from the running inbound only; clients.store.json still lists them. Without a
        // follow-up adu, reconnects fail (unknown UUID). Re-push active store rows so "kick" = drop session, keep credential.
        var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
        var store = await LoadStoreAsync(dataDir, cancellationToken);
        var client = store.FirstOrDefault(c =>
            !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase));
        if (client is null)
            return;

        var userJson = BuildAddUserJson(client.CommonName, client.Uuid, client.Flow ?? "");
        await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken);
        logger.LogInformation("Kick: re-added {CommonName} to running Xray after rmu.", commonName);
    }

    public async Task RehydrateRunningXrayFromStoreAsync(string dataDir, CancellationToken cancellationToken)
    {
        dataDir = Path.GetFullPath(dataDir);
        var store = await LoadStoreAsync(dataDir, cancellationToken);
        var active = store.Where(c => !c.IsRevoked).ToList();
        if (active.Count == 0)
        {
            logger.LogInformation("Xray store rehydrate: no active clients in store.");
            return;
        }

        logger.LogInformation("Xray store rehydrate: pushing {Count} client(s) to running Xray via adu.", active.Count);
        foreach (var c in active)
        {
            try
            {
                var userJson = BuildAddUserJson(c.CommonName, c.Uuid, c.Flow ?? "");
                await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken);
                logger.LogInformation("Rehydrated Xray VLESS client {CommonName} (UUID={Uuid}).", c.CommonName, c.Uuid);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Rehydrate failed for {CommonName} (UUID={Uuid}); client may already exist or Xray API error.",
                    c.CommonName, c.Uuid);
            }
        }
    }

    public async Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string dataDir,
        CancellationToken cancellationToken)
    {
        var list = await LoadStoreAsync(dataDir, cancellationToken);
        return list.Where(c => !c.IsRevoked).Select(MapToServerCertificate).ToList();
    }

    public async Task<ServerCertificate> BuildCertificateAsync(string dataDir, CancellationToken cancellationToken,
        string commonName = "client1", int certExpireDays = 365)
    {
        dataDir = Path.GetFullPath(dataDir);
        var store = await LoadStoreAsync(dataDir, cancellationToken);
        if (store.Any(c => !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Client with CommonName '{commonName}' already exists.");

        var uuid = Guid.NewGuid().ToString();
        var flow = DefaultFlow;
        var client = new StoredXRayClient
        {
            CommonName = commonName,
            Uuid = uuid,
            CreatedUtc = DateTime.UtcNow,
            Flow = flow
        };

        var userJson = BuildAddUserJson(commonName, uuid, flow);
        await xrayApi.RunApiVerbAsync(["adu", "stdin:"], userJson, cancellationToken);

        store.Add(client);
        await SaveStoreAsync(dataDir, store, cancellationToken);

        logger.LogInformation("XRay VLESS client created: CN={CommonName}, UUID={Uuid}", commonName, uuid);
        return MapToServerCertificate(client);
    }

    public async Task<ServerCertificate> RevokeCertificateAsync(string dataDir, string commonName,
        CancellationToken cancellationToken)
    {
        dataDir = Path.GetFullPath(dataDir);
        var store = await LoadStoreAsync(dataDir, cancellationToken);
        var client = store.FirstOrDefault(c =>
            !c.IsRevoked && string.Equals(c.CommonName, commonName, StringComparison.OrdinalIgnoreCase));
        if (client is null)
            throw new InvalidOperationException($"Client '{commonName}' not found.");

        await xrayApi.RunApiVerbAsync(["rmu", $"-tag={InboundTag}", commonName], null, cancellationToken);

        client.IsRevoked = true;
        client.RevokedUtc = DateTime.UtcNow;
        await SaveStoreAsync(dataDir, store, cancellationToken);

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
            Message = "VLESS client"
        };

    private static async Task<List<StoredXRayClient>> LoadStoreAsync(string dataDir, CancellationToken ct)
    {
        var path = GetStorePath(dataDir);
        if (!File.Exists(path))
            return new List<StoredXRayClient>();

        await using var fs = File.OpenRead(path);
        var list = await JsonSerializer.DeserializeAsync<List<StoredXRayClient>>(fs, JsonOpts, ct);
        return list ?? new List<StoredXRayClient>();
    }

    private static async Task SaveStoreAsync(string dataDir, List<StoredXRayClient> store, CancellationToken ct)
    {
        var path = GetStorePath(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, store, JsonOpts, ct);
    }

    private static string GetStorePath(string dataDir) =>
        Path.Combine(Path.GetFullPath(dataDir), "xray", "clients.store.json");
}
