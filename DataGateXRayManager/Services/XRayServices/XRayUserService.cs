using System.Text.Json;
using Newtonsoft.Json.Linq;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;

namespace DataGateXRayManager.Services.XRayServices;

public class XRayUserService(
    IConfiguration configuration,
    XRayProcessApi xrayApi,
    ILogger<XRayUserService> logger) : IXRayUserService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private string InboundTag => configuration["XRay:InboundTag"] ?? "vless-in";

    private string DefaultFlow => configuration["XRay:DefaultClientFlow"] ?? "";

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
        await xrayApi.RunApiAsync("adu", userJson, cancellationToken);

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

        var removeJson = new JObject
        {
            ["inboundTag"] = InboundTag,
            ["email"] = commonName
        }.ToString(Newtonsoft.Json.Formatting.None);
        await xrayApi.RunApiAsync("rmu", removeJson, cancellationToken);

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

        // Default payload for VLESS (HandlerService AddUser): matches current Xray-core VLESS account shape.
        var account = new JObject { ["id"] = uuid };
        if (!string.IsNullOrWhiteSpace(flow))
            account["flow"] = flow;

        var user = new JObject
        {
            ["email"] = email,
            ["level"] = 0,
            ["account"] = account
        };

        var root = new JObject
        {
            ["inboundTag"] = InboundTag,
            ["user"] = user
        };
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
