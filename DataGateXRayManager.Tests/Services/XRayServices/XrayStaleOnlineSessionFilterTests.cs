using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public sealed class XrayStaleOnlineSessionFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetainActive_KeepsUserWhoseLastSeenIsFresh()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");

        var kept = filter.RetainActive([Observation("fresh", 1_000, Now.AddSeconds(-30))], Now);

        Assert.Equal(["fresh"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_DropsUserWhoseLastSeenAndTrafficBothStalled()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("leaked", 1_000, lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("leaked", 1_000, lastSeen)], Now);

        Assert.Empty(kept);
    }

    [Fact]
    public void RetainActive_KeepsMuxUserWithFrozenLastSeenButGrowingTraffic()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("mux", 1_000, lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("mux", 2_000, lastSeen)], Now);

        Assert.Equal(["mux"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_KeepsUserSeenForTheFirstTimeEvenWithOldLastSeen()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");

        var kept = filter.RetainActive([Observation("just-restarted", 1_000, Now.AddHours(-2))], Now);

        Assert.Equal(["just-restarted"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_WhenWindowDisabled_KeepsEverything()
    {
        var filter = CreateFilter(staleAfterSeconds: "0");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("leaked", 1_000, lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("leaked", 1_000, lastSeen)], Now);

        Assert.Equal(["leaked"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_ForgetsTrafficOfUsersThatWentAway()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("reconnecting", 1_000, lastSeen)], Now.AddSeconds(-700));
        filter.RetainActive([], Now.AddSeconds(-650));

        // Same counters as before the gap: without pruning the stalled mark would drop the new session.
        var kept = filter.RetainActive([Observation("reconnecting", 1_000, lastSeen)], Now);

        Assert.Equal(["reconnecting"], kept.Select(c => c.Email));
    }

    private static XrayOnlineClientObservation Observation(string email, long totalBytes, DateTimeOffset? lastSeen) =>
        new(
            new XrayClientSessionDto
            {
                Email = email,
                Username = email,
                RemoteAddress = "203.0.113.10",
                BytesReceived = totalBytes,
                BytesSent = 0,
                ConnectedSince = lastSeen ?? Now
            },
            lastSeen);

    private static XrayStaleOnlineSessionFilter CreateFilter(string staleAfterSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [XrayStaleOnlineSessionFilter.StaleAfterSecondsKey] = staleAfterSeconds
            })
            .Build();

        return new XrayStaleOnlineSessionFilter(configuration, NullLogger<XrayStaleOnlineSessionFilter>.Instance);
    }
}
