using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XRayUserServiceDnsIdentityTests
{
    [Fact]
    public async Task BuildCertificateAsync_IdentityEnabled_AllocatesIpAndCallsSync_NotAdu()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var (sut, api, sync, store) = CreateSut(dataDir, identityEnabled: true);

            var cert = await sut.BuildCertificateAsync(dataDir, CancellationToken.None, "user-a");

            Assert.Equal("user-a", cert.CommonName);
            var loaded = await store.LoadAsync(dataDir, CancellationToken.None);
            Assert.Equal("10.80.0.2", loaded.Single(c => !c.IsRevoked).IdentityIp);
            sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
            api.Verify(
                x => x.RunApiVerbAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<XRayApiCallOptions>()),
                Times.Never);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task BuildCertificateAsync_IdentityDisabled_CallsAduWithoutSync()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var (sut, api, sync, store) = CreateSut(dataDir, identityEnabled: false);
            api.Setup(x => x.RunApiVerbAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<XRayApiCallOptions>()))
                .ReturnsAsync("ok");

            await sut.BuildCertificateAsync(dataDir, CancellationToken.None, "user-b");

            var loaded = await store.LoadAsync(dataDir, CancellationToken.None);
            Assert.Null(loaded.Single().IdentityIp);
            sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
            api.Verify(
                x => x.RunApiVerbAsync(
                    It.Is<IReadOnlyList<string>>(a => a[0] == "adu"),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<XRayApiCallOptions>()),
                Times.Once);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RevokeCertificateAsync_IdentityEnabled_ClearsIpAndSyncs()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var storeLock = new XrayClientStoreLock();
            var store = new XrayClientStore(storeLock);
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "user-c",
                    Uuid = "uuid-c",
                    IdentityIp = "10.80.0.5",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var (sut, api, sync, _) = CreateSut(dataDir, identityEnabled: true, store, storeLock);
            api.Setup(x => x.RunApiVerbAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<XRayApiCallOptions>()))
                .ReturnsAsync("ok");

            await sut.RevokeCertificateAsync(dataDir, "user-c", CancellationToken.None);

            var loaded = await store.LoadAsync(dataDir, CancellationToken.None);
            Assert.True(loaded.Single().IsRevoked);
            Assert.Null(loaded.Single().IdentityIp);
            sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
            api.Verify(
                x => x.RunApiVerbAsync(
                    It.Is<IReadOnlyList<string>>(a => a[0] == "rmu"),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<XRayApiCallOptions>()),
                Times.Once);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    private static (
        XRayUserService Sut,
        Mock<IXRayProcessApiRunner> Api,
        Mock<IXrayDnsIdentitySyncService> Sync,
        IXrayClientStore Store) CreateSut(
        string dataDir,
        bool identityEnabled,
        IXrayClientStore? store = null,
        IXrayClientStoreLock? storeLock = null)
    {
        storeLock ??= new XrayClientStoreLock();
        store ??= new XrayClientStore(storeLock);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XRAY_DNS_IDENTITY_SUBNET"] = "10.80.0.0/24",
            ["XRay:InboundTag"] = "vless-in"
        }).Build();

        var paths = new Mock<IDataPathResolver>();
        paths.Setup(x => x.GetDataPath()).Returns(dataDir);

        var api = new Mock<IXRayProcessApiRunner>();
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(identityEnabled);
        sync.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new XRayUserService(
            config,
            paths.Object,
            api.Object,
            store,
            storeLock,
            sync.Object,
            NullLogger<XRayUserService>.Instance);

        return (sut, api, sync, store);
    }
}
