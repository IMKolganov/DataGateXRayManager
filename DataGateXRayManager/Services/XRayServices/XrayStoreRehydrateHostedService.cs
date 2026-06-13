using DataGateXRayManager.Helpers;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// After container/Xray restart, running core only loads <c>clients: []</c> from <c>config.json</c>.
/// This service replays <see cref="IXRayUserService.RehydrateRunningXrayFromStoreAsync"/> from disk store once at startup.
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
            var users = scope.ServiceProvider.GetRequiredService<IXRayUserService>();
            var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
            await users.RehydrateRunningXrayFromStoreAsync(dataDir, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xray store rehydrate hosted service failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
