namespace DataGateXRayManager.Services.XRayServices;

public interface IXRayProcessApiRunner
{
    Task<string> RunApiVerbAsync(
        IReadOnlyList<string> verbAndArgs,
        string? stdinBody,
        CancellationToken cancellationToken,
        XRayApiCallOptions callOptions);
}
