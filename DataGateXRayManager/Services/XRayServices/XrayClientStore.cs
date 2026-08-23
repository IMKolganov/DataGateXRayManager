using Newtonsoft.Json;

namespace DataGateXRayManager.Services.XRayServices;

public interface IXrayClientStore
{
    Task<List<StoredXRayClient>> LoadAsync(string dataDir, CancellationToken cancellationToken);
    Task SaveAsync(string dataDir, List<StoredXRayClient> store, CancellationToken cancellationToken);

    /// <summary>Load without taking <see cref="IXrayClientStoreLock"/> (caller must already hold it).</summary>
    Task<List<StoredXRayClient>> LoadUnlockedAsync(string dataDir, CancellationToken cancellationToken);

    /// <summary>Save without taking <see cref="IXrayClientStoreLock"/> (caller must already hold it).</summary>
    Task SaveUnlockedAsync(string dataDir, List<StoredXRayClient> store, CancellationToken cancellationToken);

    string GetStorePath(string dataDir);
}

public sealed class XrayClientStore(IXrayClientStoreLock storeLock) : IXrayClientStore
{
    private static readonly JsonSerializerSettings JsonOpts = new() { Formatting = Formatting.Indented };

    public string GetStorePath(string dataDir) =>
        Path.Combine(Path.GetFullPath(dataDir), "xray", "clients.store.json");

    public async Task<List<StoredXRayClient>> LoadAsync(string dataDir, CancellationToken cancellationToken)
    {
        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(dataDir, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            storeLock.Release();
        }
    }

    public async Task SaveAsync(string dataDir, List<StoredXRayClient> store, CancellationToken cancellationToken)
    {
        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(dataDir, store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            storeLock.Release();
        }
    }

    public async Task<List<StoredXRayClient>> LoadUnlockedAsync(string dataDir, CancellationToken cancellationToken)
    {
        var path = GetStorePath(dataDir);
        if (!File.Exists(path))
            return [];

        await using var fs = File.OpenRead(path);
        using var reader = new StreamReader(fs);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<List<StoredXRayClient>>(json, JsonOpts) ?? [];
    }

    public async Task SaveUnlockedAsync(string dataDir, List<StoredXRayClient> store, CancellationToken cancellationToken)
    {
        var path = GetStorePath(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonConvert.SerializeObject(store, JsonOpts);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
