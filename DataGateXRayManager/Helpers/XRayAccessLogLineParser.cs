using System.Globalization;
using System.Text.Json;
using DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent;

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
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            dto = new AccessLogEventDto { RawJson = line };

            if (root.TryGetProperty("time", out var t))
            {
                var s = t.GetString();
                if (!string.IsNullOrEmpty(s) &&
                    DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    dto.TimeUtc = parsed;
            }

            dto.UserEmail = GetString(root, "email", "user", "account");

            if (dto.UserEmail is null && root.TryGetProperty("account", out var acc) && acc.ValueKind == JsonValueKind.Object &&
                acc.TryGetProperty("email", out var ae))
                dto.UserEmail = ae.GetString();

            dto.InboundTag = GetNestedTag(root, "inbound", "inboundTag");
            dto.OutboundTag = GetNestedTag(root, "outbound", "outboundTag");

            TryFillEndpoint(root, "source", dto);
            TryFillEndpoint(root, "from", dto);
            TryFillEndpoint(root, "local", dto);

            dto.Network = GetString(root, "network", "type");

            if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String)
                dto.Destination = target.GetString();
            else if (root.TryGetProperty("dest", out var dest))
                dto.Destination = dest.ValueKind == JsonValueKind.String ? dest.GetString() : dest.GetRawText();

            TryFillBytes(root, dto);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void TryFillBytes(JsonElement root, AccessLogEventDto dto)
    {
        if (!root.TryGetProperty("traffic", out var tr) || tr.ValueKind != JsonValueKind.Object)
            return;
        if (tr.TryGetProperty("uplink", out var up) && up.TryGetInt64(out var ul))
            dto.BytesUp = ul;
        if (tr.TryGetProperty("downlink", out var down) && down.TryGetInt64(out var dl))
            dto.BytesDown = dl;
    }

    private static void TryFillEndpoint(JsonElement root, string objectName, AccessLogEventDto dto)
    {
        if (!root.TryGetProperty(objectName, out var ep) || ep.ValueKind != JsonValueKind.Object)
            return;
        if (dto.ClientIp is not null)
            return;

        if (ep.TryGetProperty("address", out var addr))
            dto.ClientIp = addr.GetString();
        else if (ep.TryGetProperty("ip", out var ip))
            dto.ClientIp = ip.GetString();

        if (ep.TryGetProperty("port", out var port) && port.TryGetInt32(out var p))
            dto.ClientPort = p;
    }

    private static string? GetNestedTag(JsonElement root, string objectName, string flatName)
    {
        if (root.TryGetProperty(flatName, out var flat) && flat.ValueKind == JsonValueKind.String)
            return flat.GetString();

        if (!root.TryGetProperty(objectName, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;

        return obj.TryGetProperty("tag", out var tag) ? tag.GetString() : null;
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var n in names)
        {
            if (root.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }

        return null;
    }
}
