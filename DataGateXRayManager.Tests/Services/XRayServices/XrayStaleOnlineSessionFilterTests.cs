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

        var kept = filter.RetainActive([Observation("fresh", received: 1_000, lastSeen: Now.AddSeconds(-30))], Now);

        Assert.Equal(["fresh"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_KeepsUserWhenOnlyLastSeenIsFresh_EvenIfTrafficIsStalled()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");

        // First poll seeds a traffic mark far in the past.
        filter.RetainActive(
            [Observation("reconnect", received: 1_000, lastSeen: Now.AddHours(-3))],
            Now.AddSeconds(-700));

        // Same traffic, but a new inbound connection refreshes lastSeen — must stay online.
        var kept = filter.RetainActive(
            [Observation("reconnect", received: 1_000, lastSeen: Now.AddSeconds(-10))],
            Now);

        Assert.Equal(["reconnect"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_DropsUserWhoseLastSeenAndTrafficBothStalled()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("leaked", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("leaked", received: 1_000, lastSeen: lastSeen)], Now);

        Assert.Empty(kept);
    }

    [Fact]
    public void RetainActive_DropsUserWithNullLastSeenOnceTrafficStallsPastWindow()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");

        filter.RetainActive([Observation("no-ips", received: 50, lastSeen: null)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("no-ips", received: 50, lastSeen: null)], Now);

        Assert.Empty(kept);
    }

    [Fact]
    public void RetainActive_KeepsAtExactStaleBoundary()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("edge", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-600));
        var kept = filter.RetainActive([Observation("edge", received: 1_000, lastSeen: lastSeen)], Now);

        // now - trafficChangedAt == 600 and the check is `<= staleAfter`.
        Assert.Equal(["edge"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_KeepsMuxUserWithFrozenLastSeenButGrowingTraffic()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("mux", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("mux", received: 2_000, lastSeen: lastSeen)], Now);

        Assert.Equal(["mux"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_CountsBytesSentGrowthAsActivity()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive(
            [Observation("downlink", received: 100, sent: 50, lastSeen: lastSeen)],
            Now.AddSeconds(-700));
        var kept = filter.RetainActive(
            [Observation("downlink", received: 100, sent: 500, lastSeen: lastSeen)],
            Now);

        Assert.Equal(["downlink"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_TracksEmailCaseInsensitivelyAcrossPolls()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("User@Host", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("user@host", received: 1_000, lastSeen: lastSeen)], Now);

        Assert.Empty(kept);
    }

    [Fact]
    public void RetainActive_KeepsUserSeenForTheFirstTimeEvenWithOldLastSeen()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");

        var kept = filter.RetainActive(
            [Observation("just-restarted", received: 1_000, lastSeen: Now.AddHours(-2))],
            Now);

        Assert.Equal(["just-restarted"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_WhenWindowDisabled_KeepsEverything()
    {
        var filter = CreateFilter(staleAfterSeconds: "0");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("leaked", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("leaked", received: 1_000, lastSeen: lastSeen)], Now);

        Assert.Equal(["leaked"], kept.Select(c => c.Email));
    }

    [Fact]
    public void RetainActive_MissingConfig_UsesDefaultWindow()
    {
        var filter = CreateFilter(staleAfterSeconds: null);
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("defaulted", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        var kept = filter.RetainActive([Observation("defaulted", received: 1_000, lastSeen: lastSeen)], Now);

        Assert.Empty(kept);
    }

    [Fact]
    public void RetainActive_ForgetsTrafficOfUsersThatWentAway()
    {
        var filter = CreateFilter(staleAfterSeconds: "600");
        var lastSeen = Now.AddHours(-2);

        filter.RetainActive([Observation("reconnecting", received: 1_000, lastSeen: lastSeen)], Now.AddSeconds(-700));
        filter.RetainActive([], Now.AddSeconds(-650));

        // Same counters as before the gap: without pruning the stalled mark would drop the new session.
        var kept = filter.RetainActive([Observation("reconnecting", received: 1_000, lastSeen: lastSeen)], Now);

        Assert.Equal(["reconnecting"], kept.Select(c => c.Email));
    }

    private static XrayOnlineClientObservation Observation(
        string email,
        long received,
        DateTimeOffset? lastSeen,
        long sent = 0) =>
        new(
            new XrayClientSessionDto
            {
                Email = email,
                Username = email,
                RemoteAddress = "203.0.113.10",
                BytesReceived = received,
                BytesSent = sent,
                ConnectedSince = lastSeen ?? Now
            },
            lastSeen);

    private static XrayStaleOnlineSessionFilter CreateFilter(string? staleAfterSeconds)
    {
        var pairs = new Dictionary<string, string?>();
        if (staleAfterSeconds is not null)
            pairs[XrayStaleOnlineSessionFilter.StaleAfterSecondsKey] = staleAfterSeconds;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
        return new XrayStaleOnlineSessionFilter(configuration, NullLogger<XrayStaleOnlineSessionFilter>.Instance);
    }
}
