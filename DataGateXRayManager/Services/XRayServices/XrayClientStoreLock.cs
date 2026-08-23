namespace DataGateXRayManager.Services.XRayServices;

/// <summary>Process-wide lock for clients.store.json + DNS identity sync (config/PID restart).</summary>
public interface IXrayClientStoreLock
{
    Task WaitAsync(CancellationToken cancellationToken);
    void Release();
}

public sealed class XrayClientStoreLock : IXrayClientStoreLock
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) => _mutex.WaitAsync(cancellationToken);

    public void Release() => _mutex.Release();
}
