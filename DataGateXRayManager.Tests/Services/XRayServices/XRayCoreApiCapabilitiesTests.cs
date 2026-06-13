using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public sealed class XRayCoreApiCapabilitiesTests
{
    [Fact]
    public async Task GetStatOnlineIpListModeAsync_WhenAllWithTrafficWorks_CachesAllWithTraffic()
    {
        var calls = 0;
        var runner = new FakeXRayProcessApiRunner((verbAndArgs, _, _, options) =>
        {
            calls++;
            Assert.Equal(XRayApiCallOptions.CapabilityProbe, options);
            Assert.Equal(["statsonlineiplist", "-all", "-include-traffic"], verbAndArgs);
            return Task.FromResult("""{"users":[]}""");
        });

        var capabilities = new XRayCoreApiCapabilities(runner, NullLogger<XRayCoreApiCapabilities>.Instance);

        var first = await capabilities.GetStatOnlineIpListModeAsync(CancellationToken.None);
        var second = await capabilities.GetStatOnlineIpListModeAsync(CancellationToken.None);

        Assert.Equal(XRayStatOnlineIpListMode.AllWithTraffic, first);
        Assert.Equal(XRayStatOnlineIpListMode.AllWithTraffic, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetStatOnlineIpListModeAsync_WhenAllUnsupported_UsesLegacyPerEmailWithoutRetryingAll()
    {
        var calls = 0;
        var runner = new FakeXRayProcessApiRunner((verbAndArgs, _, _, _) =>
        {
            calls++;
            Assert.Equal(["statsonlineiplist", "-all", "-include-traffic"], verbAndArgs);
            throw new InvalidOperationException("xray api failed (2): flag provided but not defined: -all");
        });

        var capabilities = new XRayCoreApiCapabilities(runner, NullLogger<XRayCoreApiCapabilities>.Instance);

        var mode = await capabilities.GetStatOnlineIpListModeAsync(CancellationToken.None);
        _ = await capabilities.GetStatOnlineIpListModeAsync(CancellationToken.None);

        Assert.Equal(XRayStatOnlineIpListMode.LegacyPerEmail, mode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetStatOnlineIpListModeAsync_WhenIncludeTrafficUnsupported_UsesAllWithoutTraffic()
    {
        var calls = 0;
        var runner = new FakeXRayProcessApiRunner((verbAndArgs, _, _, _) =>
        {
            calls++;
            return verbAndArgs switch
            {
                ["statsonlineiplist", "-all", "-include-traffic"] =>
                    throw new InvalidOperationException(
                        "xray api failed (2): flag provided but not defined: -include-traffic"),
                ["statsonlineiplist", "-all"] => Task.FromResult("""{"users":[]}"""),
                _ => throw new InvalidOperationException($"Unexpected argv: {string.Join(' ', verbAndArgs)}")
            };
        });

        var capabilities = new XRayCoreApiCapabilities(runner, NullLogger<XRayCoreApiCapabilities>.Instance);

        var mode = await capabilities.GetStatOnlineIpListModeAsync(CancellationToken.None);

        Assert.Equal(XRayStatOnlineIpListMode.AllWithoutTraffic, mode);
        Assert.Equal(2, calls);
    }

    private sealed class FakeXRayProcessApiRunner(
        Func<IReadOnlyList<string>, string?, CancellationToken, XRayApiCallOptions, Task<string>> handler)
        : IXRayProcessApiRunner
    {
        public Task<string> RunApiVerbAsync(
            IReadOnlyList<string> verbAndArgs,
            string? stdinBody,
            CancellationToken cancellationToken,
            XRayApiCallOptions callOptions) =>
            handler(verbAndArgs, stdinBody, cancellationToken, callOptions);
    }
}
