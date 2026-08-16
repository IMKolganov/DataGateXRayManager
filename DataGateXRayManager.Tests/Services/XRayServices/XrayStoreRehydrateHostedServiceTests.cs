using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayStoreRehydrateHostedServiceTests
{
    [Fact]
    public async Task StartAsync_IdentityEnabled_CallsSyncOnly()
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

        // Hosted service delays 2s — override by calling StartAsync with cancelled... can't skip delay.
        // Use reflection-free approach: accept 2s delay in test.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);

        sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartAsync_IdentityDisabled_CallsRehydrate()
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);

        users.Verify(x => x.RehydrateRunningXrayFromStoreAsync("/data", It.IsAny<CancellationToken>()), Times.Once);
        sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
