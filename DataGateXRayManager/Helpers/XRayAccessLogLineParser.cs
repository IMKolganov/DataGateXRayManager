using System.Globalization;
using DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent;
using Newtonsoft.Json.Linq;

namespace DataGateXRayManager.Helpers;

public static class XRayAccessLogLineParser
{
    public static bool TryParseStructured(string line, out AccessLogEventDto? dto)
    {
        dto = null;
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{')
            return false;

        try
        {
            var root = JObject.Parse(line);
            dto = new AccessLogEventDto { RawJson = line };

            var time = root["time"]?.ToString();
            if (!string.IsNullOrEmpty(time) &&
                DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                dto.TimeUtc = parsed;

            dto.UserEmail = GetString(root, "email", "user", "account");

            if (dto.UserEmail is null && root["account"] is JObject acc)
                dto.UserEmail = acc["email"]?.ToString();

            dto.InboundTag = GetNestedTag(root, "inbound", "inboundTag");
            dto.OutboundTag = GetNestedTag(root, "outbound", "outboundTag");

            TryFillEndpoint(root, "source", dto);
            TryFillEndpoint(root, "from", dto);
            TryFillEndpoint(root, "local", dto);

            dto.Network = GetString(root, "network", "type");

            if (root["target"] is JValue { Type: JTokenType.String } target)
                dto.Destination = target.ToString();
            else if (root["dest"] is JToken dest)
                dto.Destination = dest.Type == JTokenType.String ? dest.ToString() : dest.ToString(Newtonsoft.Json.Formatting.None);

            TryFillBytes(root, dto);

            return true;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }

    private static void TryFillBytes(JObject root, AccessLogEventDto dto)
    {
        if (root["traffic"] is not JObject tr)
            return;
        if (tr["uplink"]?.Value<long?>() is { } ul)
            dto.BytesUp = ul;
        if (tr["downlink"]?.Value<long?>() is { } dl)
            dto.BytesDown = dl;
    }

    private static void TryFillEndpoint(JObject root, string objectName, AccessLogEventDto dto)
    {
        if (root[objectName] is not JObject ep || dto.ClientIp is not null)
            return;

        dto.ClientIp = ep["address"]?.ToString() ?? ep["ip"]?.ToString();
        if (ep["port"]?.Value<int?>() is { } p)
            dto.ClientPort = p;
    }

    private static string? GetNestedTag(JObject root, string objectName, string flatName)
    {
        if (root[flatName]?.Type == JTokenType.String)
            return root[flatName]!.ToString();

        if (root[objectName] is not JObject obj)
            return null;

        return obj["tag"]?.ToString();
    }

    private static string? GetString(JObject root, params string[] names)
    {
        foreach (var n in names)
        {
            if (root[n]?.Type == JTokenType.String)
                return root[n]!.ToString();
        }

        return null;
    }
}
