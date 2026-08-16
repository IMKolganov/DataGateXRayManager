namespace DataGateXRayManager.Services.PiHole;

public static class PiHoleSubnetFilter
{
    /// <summary>
    /// When <paramref name="subnetPrefix"/> is empty and <paramref name="requirePrefix"/> is true,
    /// returns no rows (safe default on a shared Pi-hole — otherwise OpenVPN client queries leak in).
    /// </summary>
    public static bool Matches(string clientIp, string? subnetPrefix, bool requirePrefix = false)
    {
        if (string.IsNullOrWhiteSpace(subnetPrefix))
            return !requirePrefix;

        return clientIp.StartsWith(subnetPrefix.Trim(), StringComparison.Ordinal);
    }

    public static IReadOnlyList<PiHoleQueryRecord> Apply(
        IEnumerable<PiHoleQueryRecord> records,
        string? subnetPrefix,
        bool requirePrefix = false)
    {
        if (string.IsNullOrWhiteSpace(subnetPrefix))
            return requirePrefix ? Array.Empty<PiHoleQueryRecord>() : records.ToList();

        var prefix = subnetPrefix.Trim();
        return records
            .Where(r => r.ClientIp.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Drop client IPs that start with any of the comma/whitespace-separated exclude prefixes.</summary>
    public static IReadOnlyList<PiHoleQueryRecord> ApplyExcludes(
        IEnumerable<PiHoleQueryRecord> records,
        string? excludePrefixesCsv)
    {
        var excludes = ParsePrefixList(excludePrefixesCsv);
        if (excludes.Count == 0)
            return records as IReadOnlyList<PiHoleQueryRecord> ?? records.ToList();

        return records
            .Where(r => !excludes.Any(ex => r.ClientIp.StartsWith(ex, StringComparison.Ordinal)))
            .ToList();
    }

    public static IReadOnlyList<string> ParsePrefixList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Array.Empty<string>();

        return csv
            .Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
