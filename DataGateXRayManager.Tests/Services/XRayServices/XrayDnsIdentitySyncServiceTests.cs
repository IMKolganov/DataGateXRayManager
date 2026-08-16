using DataGateXRayManager.Helpers;
using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayDnsIdentitySyncServiceTests
{
    [Fact]
    public async Task SyncAsync_Disabled_DoesNotRunScriptOrTouchStore()
    {
        var config = Config(("XRAY_DNS_IDENTITY_ENABLED", "false"));
        var runner = new Mock<IXrayDnsIdentityScriptRunner>(MockBehavior.Strict);
        var store = new Mock<IXrayClientStore>(MockBehavior.Strict);
        var paths = new Mock<IDataPathResolver>();
        paths.Setup(x => x.GetDataPath()).Returns("/tmp");

        var sut = CreateSut(config, paths.Object, store.Object, runner.Object, Mock.Of<IXRayUserService>());
        await sut.SyncAsync(CancellationToken.None);

        runner.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAsync_Enabled_RunsScriptAndRehydrates()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var realStore = new XrayClientStore(new XrayClientStoreLock());
            await realStore.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "cn-1",
                    Uuid = Guid.NewGuid().ToString(),
                    IdentityIp = "10.80.0.2",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var config = Config(
                ("XRAY_DNS_IDENTITY_ENABLED", "true"),
                ("XRAY_DNS_IDENTITY_SUBNET", "10.80.0.0/24"),
                ("XRAY_DNS_IDENTITY_SYNC_SCRIPT", "/bin/true"),
                ("XRayManagement:Host", "127.0.0.1"),
                ("XRayManagement:Port", "1"));

            var paths = new Mock<IDataPathResolver>();
            paths.Setup(x => x.GetDataPath()).Returns(dataDir);

            XrayDnsIdentityScriptRequest? seen = null;
            var runner = new Mock<IXrayDnsIdentityScriptRunner>();
            runner.Setup(x => x.RunAsync(It.IsAny<XrayDnsIdentityScriptRequest>(), It.IsAny<CancellationToken>()))
                .Callback<XrayDnsIdentityScriptRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(new XrayDnsIdentityScriptResult { ExitCode = 0, Stdout = "ok", Stderr = "" });

            var users = new Mock<IXRayUserService>();
            users.Setup(x => x.RehydrateClientsAsync(It.IsAny<IReadOnlyList<StoredXRayClient>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var sut = CreateSut(config, paths.Object, realStore, runner.Object, users.Object);
            await sut.SyncAsync(CancellationToken.None);

            Assert.NotNull(seen);
            Assert.Contains("10.80.0.2", seen!.ClientsJson);
            Assert.Contains("cn-1", seen.ClientsJson);
            Assert.Equal("10.80.0.0/24", seen.Subnet);
            users.Verify(x => x.RehydrateClientsAsync(It.IsAny<IReadOnlyList<StoredXRayClient>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SyncAsync_CoalescesConcurrentCallers_IntoOneScriptRun()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var realStore = new XrayClientStore(new XrayClientStoreLock());
            await realStore.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "cn-1",
                    Uuid = Guid.NewGuid().ToString(),
                    IdentityIp = "10.80.0.2",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var config = Config(
                ("XRAY_DNS_IDENTITY_ENABLED", "true"),
                ("XRAY_DNS_IDENTITY_SUBNET", "10.80.0.0/24"),
                ("XRayManagement:Host", "127.0.0.1"),
                ("XRayManagement:Port", "1"));

            var paths = new Mock<IDataPathResolver>();
            paths.Setup(x => x.GetDataPath()).Returns(dataDir);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner = new Mock<IXrayDnsIdentityScriptRunner>();
            runner.Setup(x => x.RunAsync(It.IsAny<XrayDnsIdentityScriptRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await gate.Task;
                    return new XrayDnsIdentityScriptResult { ExitCode = 0, Stdout = "ok", Stderr = "" };
                });

            var users = new Mock<IXRayUserService>();
            users.Setup(x => x.RehydrateClientsAsync(It.IsAny<IReadOnlyList<StoredXRayClient>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var sut = CreateSut(config, paths.Object, realStore, runner.Object, users.Object);
            var first = sut.SyncAsync(CancellationToken.None);
            await Task.Delay(50);
            var second = sut.SyncAsync(CancellationToken.None);
            gate.SetResult();
            await Task.WhenAll(first, second);

            runner.Verify(x => x.RunAsync(It.IsAny<XrayDnsIdentityScriptRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static XrayDnsIdentitySyncService CreateSut(
        IConfiguration config,
        IDataPathResolver paths,
        IXrayClientStore store,
        IXrayDnsIdentityScriptRunner runner,
        IXRayUserService users)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => users);
        var sp = services.BuildServiceProvider();
        var piHole = new Mock<IPiHoleRuntimeOptionsStore>();
        piHole.Setup(x => x.GetEffective()).Returns(new PiHoleOptions { ClientSubnetPrefix = "10.80.0." });
        return new XrayDnsIdentitySyncService(
            config,
            paths,
            store,
            new XrayClientStoreLock(),
            runner,
            piHole.Object,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<XrayDnsIdentitySyncService>.Instance);
    }
}
