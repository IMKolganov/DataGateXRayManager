using System.Collections.Concurrent;
using System.Globalization;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Drops users that Xray-core still reports as online although their connection is gone.
/// Since Xray-core 26.x the online map is refcounted per inbound connection with no expiry
/// (<c>app/stats/online_map.go</c>) and the VLESS inbound never applies <c>policy.timeout.connIdle</c>,
/// so a socket that stays in ESTABLISHED after the peer disappeared keeps the entry — and its frozen
/// <c>lastSeen</c> keeps our session key stable, which is what makes the dashboard row hang forever.
/// A long-lived mux connection also freezes <c>lastSeen</c> while being perfectly alive, so a user only
/// counts as gone once neither <c>lastSeen</c> nor the traffic counters moved within the configured window.
/// </summary>
public sealed class XrayStaleOnlineSessionFilter(
    IConfiguration configuration,
    ILogger<XrayStaleOnlineSessionFilter> logger) : IXrayStaleOnlineSessionFilter
{
    internal const string StaleAfterSecondsKey = "XRAY_ONLINE_SESSION_STALE_AFTER_SECONDS";
    internal const int DefaultStaleAfterSeconds = 600;

    private readonly ConcurrentDictionary<string, TrafficMark> _trafficMarks = new(StringComparer.OrdinalIgnoreCase);

    public List<XrayClientSessionDto> RetainActive(
        IReadOnlyList<XrayOnlineClientObservation> observations,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var staleAfter = ResolveStaleAfter();
        var kept = new List<XrayClientSessionDto>(observations.Count);
        var observedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var observation in observations)
        {
            var session = observation.Session;
            var email = session.Email?.Trim() ?? string.Empty;
            observedEmails.Add(email);

            var trafficChangedAt = MarkTraffic(email, session.BytesReceived + session.BytesSent, nowUtc);
            var lastActivity = Latest(observation.LastSeenUtc, trafficChangedAt);

            if (staleAfter <= TimeSpan.Zero || nowUtc - lastActivity <= staleAfter)
            {
                kept.Add(session);
                continue;
            }

            logger.LogInformation(
                "Dropping stale xray online entry for {Email}: no new connection since {LastSeen}, "
                + "no traffic since {TrafficChangedAt}, window {StaleAfter}.",
                email, observation.LastSeenUtc, trafficChangedAt, staleAfter);
        }

        foreach (var tracked in _trafficMarks.Keys)
        {
            if (!observedEmails.Contains(tracked))
                _trafficMarks.TryRemove(tracked, out _);
        }

        return kept;
    }

    private DateTimeOffset MarkTraffic(string email, long totalBytes, DateTimeOffset nowUtc)
    {
        var mark = _trafficMarks.AddOrUpdate(
            email,
            _ => new TrafficMark(totalBytes, nowUtc),
            (_, previous) => previous.TotalBytes == totalBytes
                ? previous
                : new TrafficMark(totalBytes, nowUtc));

        return mark.ChangedAtUtc;
    }

    private TimeSpan ResolveStaleAfter()
    {
        var raw = configuration[StaleAfterSecondsKey]
                  ?? Environment.GetEnvironmentVariable(StaleAfterSecondsKey);

        if (!string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);

        return TimeSpan.FromSeconds(DefaultStaleAfterSeconds);
    }

    private static DateTimeOffset Latest(DateTimeOffset? lastSeenUtc, DateTimeOffset trafficChangedAt) =>
        lastSeenUtc.HasValue && lastSeenUtc.Value > trafficChangedAt ? lastSeenUtc.Value : trafficChangedAt;

    private readonly record struct TrafficMark(long TotalBytes, DateTimeOffset ChangedAtUtc);
}
