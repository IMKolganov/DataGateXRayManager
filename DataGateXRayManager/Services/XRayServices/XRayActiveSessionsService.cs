using System.Globalization;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using Newtonsoft.Json.Linq;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Uses Xray-core CLI <c>xray api statsonlineiplist -all -include-traffic</c> (gRPC <c>GetUsersStats</c>) to list online users with per-user traffic.
/// </summary>
public sealed class XRayActiveSessionsService(XRayProcessApi xrayApi, ILogger<XRayActiveSessionsService> logger)
    : IXRayActiveSessionsService
{
    public async Task<XrayClientsEnvelope> GetActiveClientsAsync(CancellationToken cancellationToken)
    {
        var polledAt = DateTimeOffset.UtcNow;
        try
        {
            var stdout = await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-all", "-include-traffic"], null,
                cancellationToken);
            var clients = ParseGetUsersStats(stdout);
            return new XrayClientsEnvelope { Clients = clients, PolledAt = polledAt };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "statsonlineiplist failed");
            return new XrayClientsEnvelope
            {
                Clients = [],
                PollError = ex.Message,
                PolledAt = polledAt
            };
        }
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
