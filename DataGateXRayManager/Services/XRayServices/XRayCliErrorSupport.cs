namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Parses Xray-core CLI stderr for Go <c>flag</c> package unknown-flag errors.
/// </summary>
public static class XRayCliErrorSupport
{
    /// <summary>
    /// True when <paramref name="message"/> matches Go's unknown-flag text for <paramref name="flag"/>.
    /// </summary>
    public static bool LooksLikeUndefinedCliFlag(string? message, string flag)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(flag))
            return false;

        if (!message.Contains("flag provided but not defined", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains($"not defined: {flag}", StringComparison.OrdinalIgnoreCase);
    }
}
