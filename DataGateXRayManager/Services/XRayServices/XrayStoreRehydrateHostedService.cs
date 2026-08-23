using DataGateXRayManager.Helpers;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// After container/Xray restart, running core only loads <c>clients: []</c> from <c>config.json</c>.
/// When DNS identity is enabled, syncs aliases/sendThrough then rehydrates; otherwise only rehydrates via adu.
/// Runs in the background so a slow/hung sync cannot block Kestrel from binding the management API.
/// </summary>
public sealed class XrayStoreRehydrateHostedService(
    IServiceScopeFactory scopeFactory,
    IDataPathResolver dataPathResolver,
    ILogger<XrayStoreRehydrateHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IXrayDnsIdentitySyncService>();
            if (sync.IsEnabled)
            {
                await sync.SyncAsync(stoppingToken);
                return;
            }

            var users = scope.ServiceProvider.GetRequiredService<IXRayUserService>();
            var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
            await users.RehydrateRunningXrayFromStoreAsync(dataDir, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // Soft-fail: keep the manager API up so ops can fix NET_ADMIN / iface / Pi-hole prefix
            // without a crash-loop. Identity routing may be degraded until the next successful Sync.
            logger.LogCritical(
                ex,
                "Xray store rehydrate / DNS identity sync failed at startup; continuing with degraded DNS identity.");
        }
    }
}
