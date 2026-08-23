using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Dto;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.PiHole;
using DataGateXRayManager.Services.XRayServices;
using Moq;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleClientIdentityResolverTests
{
    [Fact]
    public async Task EnrichAsync_MapsIdentityIpToCommonName()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var store = new XrayClientStore(new XrayClientStoreLock());
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "adg-76",
                    Uuid = Guid.NewGuid().ToString(),
                    IdentityIp = "10.80.0.7",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var paths = new Mock<IDataPathResolver>();
            paths.Setup(x => x.GetDataPath()).Returns(dataDir);

            var sut = new PiHoleClientIdentityResolver(paths.Object, store);
            var enriched = await sut.EnrichAsync(
            [
                new PiHoleQueryRecord(1, "10.80.0.7", "example.com", "A", "OK", DateTimeOffset.UtcNow),
                new PiHoleQueryRecord(2, "10.51.15.4", "other.com", "A", "OK", DateTimeOffset.UtcNow)
            ], CancellationToken.None);

            Assert.Equal(2, enriched.Count);
            Assert.Equal("adg-76", enriched[0].CommonName);
            Assert.Null(enriched[1].CommonName);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task EnrichAsync_StripsPortFromClientIp()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var store = new XrayClientStore(new XrayClientStoreLock());
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "user-cn",
                    Uuid = Guid.NewGuid().ToString(),
                    IdentityIp = "10.80.0.7",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var paths = new Mock<IDataPathResolver>();
            paths.Setup(x => x.GetDataPath()).Returns(dataDir);

            var sut = new PiHoleClientIdentityResolver(paths.Object, store);
            var enriched = await sut.EnrichAsync(
            [
                new PiHoleQueryRecord(1, "10.80.0.7:5353", "example.com", "A", "OK", DateTimeOffset.UtcNow)
            ], CancellationToken.None);

            Assert.Equal("user-cn", enriched[0].CommonName);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }
}
