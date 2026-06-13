namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Controls how <see cref="XRayProcessApi"/> logs non-zero CLI exits.
/// Capability probes use <see cref="CapabilityProbe"/> so expected unsupported flags
/// do not emit warning-level noise before fallback logic runs.
/// </summary>
public sealed class XRayApiCallOptions
{
    public static XRayApiCallOptions Default { get; } = new();

    public static XRayApiCallOptions CapabilityProbe { get; } = new() { LogFailedExitAsDebug = true };

    public bool LogFailedExitAsDebug { get; init; }
}
