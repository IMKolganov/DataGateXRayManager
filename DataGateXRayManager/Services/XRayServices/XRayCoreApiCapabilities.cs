namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Probes and caches Xray-core CLI capabilities once per process so polling does not
/// retry unsupported flags on every request.
/// </summary>
public sealed class XRayCoreApiCapabilities(
    IXRayProcessApiRunner xrayApi,
    ILogger<XRayCoreApiCapabilities> logger)
{
    private XRayStatOnlineIpListMode? _statOnlineIpListMode;
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    public async Task<XRayStatOnlineIpListMode> GetStatOnlineIpListModeAsync(CancellationToken cancellationToken)
    {
        if (_statOnlineIpListMode is { } known)
            return known;

        await _probeLock.WaitAsync(cancellationToken);
        try
        {
            if (_statOnlineIpListMode is { } knownAfterLock)
                return knownAfterLock;

            _statOnlineIpListMode = await ProbeStatOnlineIpListModeAsync(cancellationToken);
            logger.LogInformation("Resolved Xray statsonlineiplist capability: {Mode}", _statOnlineIpListMode);
            return _statOnlineIpListMode.Value;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private async Task<XRayStatOnlineIpListMode> ProbeStatOnlineIpListModeAsync(CancellationToken cancellationToken)
    {
        var probe = XRayApiCallOptions.CapabilityProbe;

        try
        {
            await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-all", "-include-traffic"], null, cancellationToken,
                probe);
            return XRayStatOnlineIpListMode.AllWithTraffic;
        }
        catch (InvalidOperationException ex)
        {
            if (XRayCliErrorSupport.LooksLikeUndefinedCliFlag(ex.Message, "-all"))
            {
                logger.LogInformation(
                    "Xray statsonlineiplist has no -all flag; active sessions will use statsgetallonlineusers + per-email polling.");
                return XRayStatOnlineIpListMode.LegacyPerEmail;
            }

            if (!XRayCliErrorSupport.LooksLikeUndefinedCliFlag(ex.Message, "-include-traffic"))
                throw;
        }

        try
        {
            await xrayApi.RunApiVerbAsync(["statsonlineiplist", "-all"], null, cancellationToken, probe);
            logger.LogInformation(
                "Xray statsonlineiplist supports -all but not -include-traffic; traffic counters may require xray api stats.");
            return XRayStatOnlineIpListMode.AllWithoutTraffic;
        }
        catch (InvalidOperationException ex)
        {
            if (XRayCliErrorSupport.LooksLikeUndefinedCliFlag(ex.Message, "-all"))
            {
                logger.LogInformation(
                    "Xray statsonlineiplist -all unsupported; active sessions will use statsgetallonlineusers + per-email polling.");
                return XRayStatOnlineIpListMode.LegacyPerEmail;
            }

            throw;
        }
    }
}
