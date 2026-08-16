using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.PiHole;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateXRayManager.Services.XRayServices;

public interface IXrayDnsIdentitySyncService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Backfill identity IPs, apply iface aliases + sendThrough routing via script, restart Xray, rehydrate clients.
    /// No-op when identity is disabled. Concurrent callers coalesce; requests within the debounce window share one restart.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken);
}

public sealed class XrayDnsIdentitySyncService(
    IConfiguration configuration,
    IDataPathResolver dataPathResolver,
    IXrayClientStore clientStore,
    IXrayClientStoreLock storeLock,
    IXrayDnsIdentityScriptRunner scriptRunner,
    IPiHoleRuntimeOptionsStore piHoleRuntimeOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<XrayDnsIdentitySyncService> logger) : IXrayDnsIdentitySyncService
{
    private static readonly HashSet<string> PublicDnsDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1"
    };

    private int _syncGeneration;
    private int _lastCompletedGeneration;

    public bool IsEnabled => IsTruthy(configuration["XRAY_DNS_IDENTITY_ENABLED"]
                                      ?? configuration["Xray:DnsIdentity:Enabled"]);

    private string Subnet => configuration["XRAY_DNS_IDENTITY_SUBNET"]
                             ?? configuration["Xray:DnsIdentity:Subnet"]
                             ?? XrayDnsIdentityAllocator.DefaultSubnetCidr;

    private string ScriptPath => configuration["XRAY_DNS_IDENTITY_SYNC_SCRIPT"]
                                 ?? configuration["Xray:DnsIdentity:SyncScript"]
                                 ?? "/scripts/xray/sync-dns-identity.sh";

    /// <summary>Wait after the last SyncAsync request before restarting Xray (coalesces sequential cert churn).</summary>
    private int DebounceMilliseconds
    {
        get
        {
            var raw = configuration["XRAY_DNS_IDENTITY_SYNC_DEBOUNCE_MS"]
                      ?? configuration["Xray:DnsIdentity:SyncDebounceMs"];
            if (int.TryParse(raw, out var ms) && ms >= 0)
                return Math.Min(ms, 60_000);
            return 2_000;
        }
    }

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            logger.LogDebug("Xray DNS identity sync skipped (XRAY_DNS_IDENTITY_ENABLED not set).");
            return;
        }

        var myTicket = Interlocked.Increment(ref _syncGeneration);
        var debounceMs = DebounceMilliseconds;
        if (debounceMs > 0)
        {
            logger.LogDebug(
                "DNS identity sync debouncing {DebounceMs}ms (ticket={Ticket}).",
                debounceMs,
                myTicket);
            await Task.Delay(debounceMs, cancellationToken).ConfigureAwait(false);
        }

        WarnIfDnsLooksPublic();
        EnsurePiHolePrefixAlignedOrThrow();

        await storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Covered by a sync that finished while we waited on debounce / lock.
            if (Volatile.Read(ref _lastCompletedGeneration) >= myTicket)
            {
                logger.LogDebug(
                    "DNS identity sync coalesced (ticket={Ticket}, lastCompleted={Last}).",
                    myTicket,
                    _lastCompletedGeneration);
                return;
            }

            var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
            var store = await clientStore.LoadUnlockedAsync(dataDir, cancellationToken).ConfigureAwait(false);
            if (XrayDnsIdentityAllocator.EnsureIdentityIps(store, Subnet))
            {
                await clientStore.SaveUnlockedAsync(dataDir, store, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Backfilled DNS identity IPs for active Xray clients (subnet={Subnet}).", Subnet);
            }

            var activeClients = store.Where(c => !c.IsRevoked).ToList();
            var clientsJson = new JArray(
                activeClients
                    .Where(c => !string.IsNullOrWhiteSpace(c.IdentityIp))
                    .Select(c => new JObject
                    {
                        ["commonName"] = c.CommonName,
                        ["identityIp"] = c.IdentityIp
                    })).ToString(Formatting.None);
            var activeCount = activeClients.Count;
            await RunSyncScriptAsync(clientsJson, cancellationToken).ConfigureAwait(false);
            await WaitForXrayApiAsync(cancellationToken).ConfigureAwait(false);

            using var scope = scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IXRayUserService>();
            var rehydrated = await users.RehydrateClientsAsync(activeClients, cancellationToken)
                .ConfigureAwait(false);
            if (activeCount > 0 && rehydrated != activeCount)
            {
                throw new InvalidOperationException(
                    $"DNS identity sync restarted Xray but rehydrate pushed {rehydrated}/{activeCount} client(s); VLESS users may be offline.");
            }

            Volatile.Write(ref _lastCompletedGeneration, Volatile.Read(ref _syncGeneration));

            logger.LogInformation(
                "DNS identity sync complete: {Count} active identity IP(s), rehydrated={Rehydrated}.",
                activeCount,
                rehydrated);
        }
        finally
        {
            storeLock.Release();
        }
    }

    private void WarnIfDnsLooksPublic()
    {
        var dns1 = configuration["DNS1"] ?? Environment.GetEnvironmentVariable("DNS1");
        if (!string.IsNullOrWhiteSpace(dns1) && PublicDnsDefaults.Contains(dns1.Trim()))
        {
            logger.LogWarning(
                "XRAY_DNS_IDENTITY_ENABLED is on but DNS1={Dns1} looks like a public resolver. " +
                "Set DNS1/DNS2 to Pi-hole and point client VPN DNS at that address through the tunnel.",
                dns1);
        }
    }

    private void EnsurePiHolePrefixAlignedOrThrow()
    {
        var prefix = piHoleRuntimeOptions.GetEffective().ClientSubnetPrefix;
        var suggested = XrayDnsIdentityAllocator.SuggestedClientSubnetPrefix(Subnet);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException(
                "XRAY_DNS_IDENTITY_ENABLED requires Pi-hole ClientSubnetPrefix matching the identity pool " +
                $"(suggested '{suggested ?? "10.80.0."}'). Empty prefix would ingest nothing or risk shared-Pi-hole mixups.");
        }

        if (XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool(prefix, Subnet))
            return;

        throw new InvalidOperationException(
            $"Pi-hole ClientSubnetPrefix '{prefix}' does not cover XRAY_DNS_IDENTITY_SUBNET '{Subnet}'. " +
            $"Use a non-overlapping subnet per node and matching prefix (suggested '{suggested ?? "(n/a)"}').");
    }

    private async Task RunSyncScriptAsync(string clientsJson, CancellationToken cancellationToken)
    {
        var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
        var configPath = configuration["CONFIG_PATH"]
                         ?? Environment.GetEnvironmentVariable("CONFIG_PATH")
                         ?? Path.Combine(dataDir, "xray", "config.json");
        var pidFile = configuration["XRAY_PID_FILE"]
                      ?? Environment.GetEnvironmentVariable("XRAY_PID_FILE")
                      ?? Path.Combine(dataDir, "xray", "xray.pid");
        var iface = configuration["XRAY_DNS_IDENTITY_IFACE"]
                    ?? configuration["Xray:DnsIdentity:Iface"]
                    ?? "eth0";

        logger.LogInformation(
            "Running DNS identity sync script {Script} (clients={Count}, iface={Iface}).",
            ScriptPath,
            JArray.Parse(clientsJson).Count,
            iface);

        var result = await scriptRunner.RunAsync(new XrayDnsIdentityScriptRequest
        {
            ScriptPath = ScriptPath,
            ConfigPath = configPath,
            PidFile = pidFile,
            Iface = iface,
            Subnet = Subnet,
            ClientsJson = clientsJson
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            logger.LogError(
                "DNS identity sync script failed (exit={Code}). stdout={Stdout} stderr={Stderr}",
                result.ExitCode,
                result.Stdout,
                result.Stderr);
            throw new InvalidOperationException(
                $"sync-dns-identity.sh exited with code {result.ExitCode}: {result.Stderr}");
        }

        if (!string.IsNullOrWhiteSpace(result.Stdout))
            logger.LogInformation("DNS identity sync script: {Output}", result.Stdout.Trim());
    }

    private async Task WaitForXrayApiAsync(CancellationToken cancellationToken)
    {
        var host = configuration["XRayManagement:Host"] ?? "127.0.0.1";
        var port = configuration["XRayManagement:Port"] ?? "10085";
        if (!int.TryParse(port, out var portNum))
            portNum = 10085;

        for (var i = 0; i < 30; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(host, portNum, cancellationToken);
                return;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        logger.LogWarning("Xray API {Host}:{Port} not open after DNS identity restart; rehydrate may fail.", host, portNum);
    }

    private static bool IsTruthy(string? value) =>
        value is not null
        && value.Trim() is "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON";
}
