using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayStoreRehydrateHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_IdentityEnabled_CallsSyncOnly()
    {
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(true);
        sync.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var users = new Mock<IXRayUserService>(MockBehavior.Strict);
        var paths = new Mock<IDataPathResolver>();
        paths.Setup(x => x.GetDataPath()).Returns("/data");

        var services = new ServiceCollection();
        services.AddSingleton(sync.Object);
        services.AddScoped(_ => users.Object);
        var sp = services.BuildServiceProvider();

        var sut = new XrayStoreRehydrateHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            paths.Object,
            NullLogger<XrayStoreRehydrateHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await sut.StartAsync(cts.Token);
        await WaitUntilAsync(() =>
        {
            try
            {
                sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch (MockException)
            {
                return false;
            }
        }, cts.Token);

        users.VerifyNoOtherCalls();
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_IdentityDisabled_CallsRehydrate()
    {
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(false);

        var users = new Mock<IXRayUserService>();
        users.Setup(x => x.RehydrateRunningXrayFromStoreAsync("/data", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var paths = new Mock<IDataPathResolver>();
        paths.Setup(x => x.GetDataPath()).Returns("/data");

        var services = new ServiceCollection();
        services.AddSingleton(sync.Object);
        services.AddScoped(_ => users.Object);
        var sp = services.BuildServiceProvider();

        var sut = new XrayStoreRehydrateHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            paths.Object,
            NullLogger<XrayStoreRehydrateHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await sut.StartAsync(cts.Token);
        await WaitUntilAsync(() =>
        {
            try
            {
                users.Verify(
                    x => x.RehydrateRunningXrayFromStoreAsync("/data", It.IsAny<CancellationToken>()),
                    Times.Once);
                return true;
            }
            catch (MockException)
            {
                return false;
            }
        }, cts.Token);

        sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
        await sut.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken);
        }
    }
}
