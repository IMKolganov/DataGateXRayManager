using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.XRayServices;

/// <summary>
/// Covers the multi-inbound push/remove path used when entrypoint sets
/// <c>XRay__ExtraInboundTags</c> after a successful xHTTP render.
/// </summary>
public sealed class XRayUserServiceExtraInboundTests
{
    [Fact]
    public async Task KickInboundUserAsync_WithExtraInboundTag_RemovesAndReaddsOnBothInbounds()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var storeLock = new XrayClientStoreLock();
            var store = new XrayClientStore(storeLock);
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "cn-xhttp",
                    Uuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    Flow = "",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var calls = new List<(IReadOnlyList<string> Args, string? Stdin)>();
            var (sut, api) = CreateSut(dataDir, store, storeLock, extraInboundTags: "vless-xhttp-in",
                onApi: (args, stdin) =>
                {
                    calls.Add((args.ToArray(), stdin));
                    return "ok";
                });

            await sut.KickInboundUserAsync("cn-xhttp", CancellationToken.None);

            Assert.Equal(4, calls.Count);
            Assert.Equal(["rmu", "-tag=vless-in", "cn-xhttp"], calls[0].Args);
            Assert.Equal(["rmu", "-tag=vless-xhttp-in", "cn-xhttp"], calls[1].Args);
            Assert.Equal(["adu", "stdin:"], calls[2].Args);
            Assert.Equal(["adu", "stdin:"], calls[3].Args);
            Assert.Contains("\"tag\":\"vless-in\"", calls[2].Stdin);
            Assert.Contains("\"tag\":\"vless-xhttp-in\"", calls[3].Stdin);
            Assert.Contains("\"email\":\"cn-xhttp\"", calls[2].Stdin);
            Assert.Contains("\"email\":\"cn-xhttp\"", calls[3].Stdin);
            api.Verify(
                x => x.RunApiVerbAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(), It.IsAny<XRayApiCallOptions>()),
                Times.Exactly(4));
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task KickInboundUserAsync_ExtraInboundAduFailure_DoesNotFailKick()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var storeLock = new XrayClientStoreLock();
            var store = new XrayClientStore(storeLock);
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "cn-ok",
                    Uuid = "11111111-2222-3333-4444-555555555555",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var callIndex = 0;
            var (sut, api) = CreateSut(dataDir, store, storeLock, extraInboundTags: "vless-xhttp-in",
                onApi: (_, _) =>
                {
                    callIndex++;
                    // 1 rmu primary, 2 rmu extra, 3 adu primary, 4 adu extra → fail only the optional one
                    if (callIndex == 4)
                        throw new InvalidOperationException("extra inbound down");
                    return "ok";
                });

            await sut.KickInboundUserAsync("cn-ok", CancellationToken.None);

            api.Verify(
                x => x.RunApiVerbAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(), It.IsAny<XRayApiCallOptions>()),
                Times.Exactly(4));
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task KickInboundUserAsync_WithoutExtraTags_OnlyTouchesPrimaryInbound()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "xray-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "xray"));
        try
        {
            var storeLock = new XrayClientStoreLock();
            var store = new XrayClientStore(storeLock);
            await store.SaveAsync(dataDir,
            [
                new StoredXRayClient
                {
                    CommonName = "cn-one",
                    Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    IsRevoked = false
                }
            ], CancellationToken.None);

            var calls = new List<IReadOnlyList<string>>();
            var (sut, _) = CreateSut(dataDir, store, storeLock, extraInboundTags: null,
                onApi: (args, _) =>
                {
                    calls.Add(args.ToArray());
                    return "ok";
                });

            await sut.KickInboundUserAsync("cn-one", CancellationToken.None);

            Assert.Equal(2, calls.Count);
            Assert.Equal(["rmu", "-tag=vless-in", "cn-one"], calls[0]);
            Assert.Equal(["adu", "stdin:"], calls[1]);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { /* ignore */ }
        }
    }

    private static (XRayUserService Sut, Mock<IXRayProcessApiRunner> Api) CreateSut(
        string dataDir,
        IXrayClientStore store,
        IXrayClientStoreLock storeLock,
        string? extraInboundTags,
        Func<IReadOnlyList<string>, string?, string> onApi)
    {
        var pairs = new Dictionary<string, string?>
        {
            ["XRay:InboundTag"] = "vless-in",
            ["XRAY_DNS_IDENTITY_SUBNET"] = "10.80.0.0/24"
        };
        if (!string.IsNullOrWhiteSpace(extraInboundTags))
            pairs["XRay:ExtraInboundTags"] = extraInboundTags;

        var config = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
        var paths = new Mock<IDataPathResolver>();
        paths.Setup(x => x.GetDataPath()).Returns(dataDir);

        var api = new Mock<IXRayProcessApiRunner>();
        api.Setup(x => x.RunApiVerbAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<XRayApiCallOptions>()))
            .ReturnsAsync((IReadOnlyList<string> args, string? stdin, CancellationToken _, XRayApiCallOptions _) =>
                onApi(args, stdin));

        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(false);

        var sut = new XRayUserService(
            config,
            paths.Object,
            api.Object,
            store,
            storeLock,
            sync.Object,
            NullLogger<XRayUserService>.Instance);

        return (sut, api);
    }
}
