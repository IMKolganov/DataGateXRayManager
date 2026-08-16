using DataGateXRayManager.Helpers;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// After container/Xray restart, running core only loads <c>clients: []</c> from <c>config.json</c>.
/// When DNS identity is enabled, syncs aliases/sendThrough then rehydrates; otherwise only rehydrates via adu.
/// </summary>
public sealed class XrayStoreRehydrateHostedService(
    IServiceScopeFactory scopeFactory,
    IDataPathResolver dataPathResolver,
    ILogger<XrayStoreRehydrateHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IXrayDnsIdentitySyncService>();
            if (sync.IsEnabled)
            {
                await sync.SyncAsync(cancellationToken);
                return;
            }

            var users = scope.ServiceProvider.GetRequiredService<IXRayUserService>();
            var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
            await users.RehydrateRunningXrayFromStoreAsync(dataDir, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Xray store rehydrate / DNS identity sync failed at startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
