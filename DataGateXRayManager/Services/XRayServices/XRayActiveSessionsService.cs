using System.Globalization;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using Newtonsoft.Json.Linq;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Polls Xray-core CLI for online VLESS clients. Prefers <c>statsonlineiplist -all -include-traffic</c> (recent cores);
/// falls back to <c>-all</c> only, then to <c>statsgetallonlineusers</c> plus per-email <c>statsonlineiplist -email …</c>
/// for older binaries whose <c>statsonlineiplist</c> only documents <c>-email</c>.
/// When JSON has no traffic (e.g. core without <c>-include-traffic</c>), fills bytes via <c>xray api stats -name …</c>
/// using standard user traffic counter names (see Xray stats documentation).
/// if user-level stats are enabled in the Xray config.
/// </summary>
public sealed class XRayActiveSessionsService(XRayProcessApi xrayApi, ILogger<XRayActiveSessionsService> logger)
    : IXRayActiveSessionsService
{
    public async Task<XrayClientsEnvelope> GetActiveClientsAsync(CancellationToken cancellationToken)
    {
        var polledAt = DateTimeOffset.UtcNow;
        try
        {
            var clients = await TryCollectOnlineClientsAsync(cancellationToken);
            return new XrayClientsEnvelope { Clients = clients, PolledAt = polledAt };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "xray online client polling failed");
            return new XrayClientsEnvelope
            {
                Clients = [],
                PollError = ex.Message,
                PolledAt = polledAt
            };
        }
    }

    private async Task<List<XrayClientSessionDto>> TryCollectOnlineClientsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stdout = await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-all", "-include-traffic"], null,
                cancellationToken);
            var withTraffic = ParseGetUsersStats(stdout);
            await EnrichZeroTrafficFromUserStatCountersAsync(withTraffic, cancellationToken);
            return withTraffic;
        }
        catch (InvalidOperationException ex)
        {
            var msg = ex.Message;
            // Older cores: no bulk -all at all — do not retry "statsonlineiplist -all" (same failure).
            if (LooksLikeUndefinedCliFlag(msg, "-all"))
            {
                logger.LogInformation(
                    "statsonlineiplist has no -all; using statsgetallonlineusers + per-email -email. {Reason}", msg);
                return await CollectViaLegacyPerEmailAsync(cancellationToken);
            }

            if (!LooksLikeUndefinedCliFlag(msg, "-include-traffic"))
                throw;

            logger.LogInformation("Retrying statsonlineiplist without -include-traffic ({Reason})", msg);
        }

        try
        {
            var stdout = await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-all"], null, cancellationToken);
            var withoutTraffic = ParseGetUsersStats(stdout);
            await EnrichZeroTrafficFromUserStatCountersAsync(withoutTraffic, cancellationToken);
            return withoutTraffic;
        }
        catch (InvalidOperationException ex)
        {
            if (!LooksLikeUndefinedCliFlag(ex.Message, "-all"))
                throw;

            logger.LogInformation(
                "statsonlineiplist -all unsupported; using statsgetallonlineusers + per-email -email. {Reason}",
                ex.Message);
        }

        return await CollectViaLegacyPerEmailAsync(cancellationToken);
    }

    /// <summary>True when stderr matches Go's <c>flag.CommandLine</c> unknown-flag text for <paramref name="flag"/>.</summary>
    private static bool LooksLikeUndefinedCliFlag(string message, string flag)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (!message.Contains("flag provided but not defined", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains($"not defined: {flag}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<XrayClientSessionDto>> CollectViaLegacyPerEmailAsync(CancellationToken cancellationToken)
    {
        var listStdout = await xrayApi.RunApiVerbAsync(["statsgetallonlineusers"], null, cancellationToken);
        var emails = ParseAllOnlineUserStatNames(listStdout);
        if (emails.Count == 0)
            return [];

        var list = new List<XrayClientSessionDto>();
        foreach (var email in emails)
        {
            try
            {
                var oneStdout =
                    await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-email", email], null, cancellationToken);
                var dto = ParseSingleUserOnlineIpList(oneStdout, email);
                if (dto is not null)
                    list.Add(dto);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "statsonlineiplist -email failed for {Email}", email);
            }
        }

        await EnrichZeroTrafficFromUserStatCountersAsync(list, cancellationToken);
        return list;
    }

    /// <summary>
    /// Per-user traffic is absent from <c>statsonlineiplist -email</c> and from <c>-all</c> without <c>-include-traffic</c>.
    /// Reads Xray user-level counters (requires stats / policy in config — see Project X docs).
    /// </summary>
    private async Task EnrichZeroTrafficFromUserStatCountersAsync(List<XrayClientSessionDto> list,
        CancellationToken cancellationToken)
    {
        foreach (var c in list)
        {
            if (c.BytesReceived != 0 || c.BytesSent != 0)
                continue;

            var email = c.Email?.Trim();
            if (string.IsNullOrEmpty(email))
                continue;

            try
            {
                var uplinkName = $"user>>>{email}>>>traffic>>>uplink";
                var downlinkName = $"user>>>{email}>>>traffic>>>downlink";
                var upOut =
                    await xrayApi.RunApiVerbAsync(["stats", "-name", uplinkName], null, cancellationToken);
                var downOut =
                    await xrayApi.RunApiVerbAsync(["stats", "-name", downlinkName], null, cancellationToken);
                c.BytesReceived = ParseStatsCounterValue(upOut);
                c.BytesSent = ParseStatsCounterValue(downOut);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex,
                    "xray api stats counters missing or failed for {Email} (enable user stats in Xray config if you need traffic here)",
                    email);
            }
        }
    }

    /// <summary>Parses <c>GetStatsResponse</c> JSON from <c>xray api stats</c>.</summary>
    private static long ParseStatsCounterValue(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return 0;

        var root = JObject.Parse(stdout);
        var stat = root["stat"] ?? root["Stat"];
        if (stat is null || stat.Type == JTokenType.Null)
            return 0;

        var v = stat["value"] ?? stat["Value"];
        if (v is null || v.Type == JTokenType.Null)
            return 0;

        if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
            return v.Value<long>();

        if (v.Type == JTokenType.String && long.TryParse(v.Value<string>(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return 0;
    }

    /// <summary>Parses <c>GetAllOnlineUsersResponse</c>: <c>{"users":["user>>>cn@host>>>online", ...]}</c>.</summary>
    private static List<string> ParseAllOnlineUserStatNames(string stdout)
    {
        var emails = new List<string>();
        if (string.IsNullOrWhiteSpace(stdout))
            return emails;

        var root = JObject.Parse(stdout);
        var users = root["users"] as JArray ?? root["Users"] as JArray;
        if (users is null)
            return emails;

        foreach (var t in users)
        {
            if (t.Type != JTokenType.String)
                continue;
            var statName = t.Value<string>();
            var email = ExtractEmailFromOnlineStatName(statName);
            if (!string.IsNullOrWhiteSpace(email))
                emails.Add(email);
        }

        return emails;
    }

    private static string? ExtractEmailFromOnlineStatName(string? statName)
    {
        if (string.IsNullOrEmpty(statName))
            return null;

        const string prefix = "user>>>";
        const string suffix = ">>>online";
        if (!statName.StartsWith(prefix, StringComparison.Ordinal)
            || !statName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        return statName.Substring(prefix.Length, statName.Length - prefix.Length - suffix.Length);
    }

    /// <summary>Parses <c>GetStatsOnlineIpListResponse</c> (single user) from older cores.</summary>
    private static XrayClientSessionDto? ParseSingleUserOnlineIpList(string stdout, string email)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        var root = JObject.Parse(stdout);
        var ipsToken = root["ips"] ?? root["Ips"];
        if (ipsToken is null || ipsToken.Type == JTokenType.Null)
        {
            return new XrayClientSessionDto
            {
                Email = email,
                RemoteAddress = "",
                Username = email,
                BytesReceived = 0,
                BytesSent = 0,
                ConnectedSince = DateTimeOffset.UtcNow
            };
        }

        DateTimeOffset? minSeen = null;
        string? primaryIp = null;

        if (ipsToken is JArray arr)
        {
            foreach (var ipEntry in arr.OfType<JObject>())
            {
                var ip = (string?)ipEntry["ip"] ?? (string?)ipEntry["Ip"] ?? "";
                if (string.IsNullOrEmpty(primaryIp))
                    primaryIp = ip;

                var ls = ipEntry["lastSeen"] ?? ipEntry["LastSeen"];
                var dto = ParseLastSeen(ls);
                if (dto.HasValue && (minSeen is null || dto.Value < minSeen))
                    minSeen = dto;
            }
        }
        else if (ipsToken is JObject map)
        {
            foreach (var prop in map.Properties())
            {
                var ip = prop.Name;
                if (string.IsNullOrEmpty(primaryIp))
                    primaryIp = ip;

                DateTimeOffset? dto = null;
                var v = prop.Value;
                if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                    dto = ParseLastSeenFromUnix(v.Value<long>());
                else if (v is JObject jo)
                    dto = ParseLastSeen(jo["lastSeen"] ?? jo["LastSeen"]);

                if (dto.HasValue && (minSeen is null || dto.Value < minSeen))
                    minSeen = dto;
            }
        }

        return new XrayClientSessionDto
        {
            Email = email,
            RemoteAddress = primaryIp ?? "",
            Username = email,
            BytesReceived = 0,
            BytesSent = 0,
            ConnectedSince = minSeen ?? DateTimeOffset.UtcNow
        };
    }

    private static DateTimeOffset? ParseLastSeenFromUnix(long v)
    {
        if (v > 10_000_000_000L)
            return DateTimeOffset.FromUnixTimeMilliseconds(v);
        if (v > 0)
            return DateTimeOffset.FromUnixTimeSeconds(v);
        return null;
    }

    private static List<XrayClientSessionDto> ParseGetUsersStats(string stdout)
    {
        var list = new List<XrayClientSessionDto>();
        if (string.IsNullOrWhiteSpace(stdout))
            return list;

        var root = JObject.Parse(stdout);
        var users = root["users"] as JArray ?? root["Users"] as JArray;
        if (users is null)
            return list;

        foreach (var u in users.OfType<JObject>())
        {
            var email = (string?)u["email"] ?? (string?)u["Email"] ?? "";
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var traffic = u["traffic"] as JObject ?? u["Traffic"] as JObject;
            var uplink = traffic?["uplink"]?.Value<long>() ?? traffic?["Uplink"]?.Value<long>() ?? 0L;
            var downlink = traffic?["downlink"]?.Value<long>() ?? traffic?["Downlink"]?.Value<long>() ?? 0L;

            var ips = u["ips"] as JArray ?? u["Ips"] as JArray ?? u["IPs"] as JArray;
            if (ips is null || ips.Count == 0)
            {
                list.Add(new XrayClientSessionDto
                {
                    Email = email,
                    RemoteAddress = "",
                    Username = email,
                    BytesReceived = uplink,
                    BytesSent = downlink,
                    ConnectedSince = DateTimeOffset.UtcNow
                });
                continue;
            }

            DateTimeOffset? minSeen = null;
            string? primaryIp = null;
            foreach (var ipEntry in ips.OfType<JObject>())
            {
                var ip = (string?)ipEntry["ip"] ?? (string?)ipEntry["Ip"] ?? "";
                if (string.IsNullOrEmpty(primaryIp))
                    primaryIp = ip;

                var ls = ipEntry["lastSeen"] ?? ipEntry["LastSeen"];
                var dto = ParseLastSeen(ls);
                if (dto.HasValue && (minSeen is null || dto.Value < minSeen))
                    minSeen = dto;
            }

            list.Add(new XrayClientSessionDto
            {
                Email = email,
                RemoteAddress = primaryIp ?? "",
                Username = email,
                BytesReceived = uplink,
                BytesSent = downlink,
                ConnectedSince = minSeen ?? DateTimeOffset.UtcNow
            });
        }

        return list;
    }

    private static DateTimeOffset? ParseLastSeen(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            var v = token.Value<long>();
            // Heuristic: ms vs s
            if (v > 10_000_000_000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(v);
            return DateTimeOffset.FromUnixTimeSeconds(v);
        }

        if (token.Type == JTokenType.String &&
            DateTimeOffset.TryParse(token.Value<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;

        return null;
    }
}
