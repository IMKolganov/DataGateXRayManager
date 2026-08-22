using System.Diagnostics;
using System.Text;

namespace DataGateXRayManager.Services.XRayServices;

public sealed class XrayDnsIdentityScriptRequest
{
    public required string ScriptPath { get; init; }
    public required string ConfigPath { get; init; }
    public required string PidFile { get; init; }
    public required string Iface { get; init; }
    public required string Subnet { get; init; }
    public required string ClientsJson { get; init; }
}

public sealed class XrayDnsIdentityScriptResult
{
    public required int ExitCode { get; init; }
    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
}

public interface IXrayDnsIdentityScriptRunner
{
    Task<XrayDnsIdentityScriptResult> RunAsync(XrayDnsIdentityScriptRequest request, CancellationToken cancellationToken);
}

public sealed class ProcessXrayDnsIdentityScriptRunner(TimeSpan? scriptTimeout = null) : IXrayDnsIdentityScriptRunner
{
    private readonly TimeSpan _scriptTimeout = scriptTimeout ?? TimeSpan.FromSeconds(90);

    public async Task<XrayDnsIdentityScriptResult> RunAsync(
        XrayDnsIdentityScriptRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ScriptPath))
            throw new FileNotFoundException($"DNS identity sync script not found: {request.ScriptPath}", request.ScriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = request.ScriptPath,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["CONFIG_PATH"] = request.ConfigPath;
        psi.Environment["XRAY_PID_FILE"] = request.PidFile;
        psi.Environment["XRAY_DNS_IDENTITY_IFACE"] = request.Iface;
        psi.Environment["XRAY_DNS_IDENTITY_SUBNET"] = request.Subnet;
        psi.Environment["CLIENTS_JSON"] = request.ClientsJson;

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start {request.ScriptPath}");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Avoid blocking the host forever if the script stalls (e.g. xray -test / ip).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_scriptTimeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore kill races
            }

            throw new TimeoutException(
                $"DNS identity sync script timed out after {_scriptTimeout.TotalSeconds:0}s: {request.ScriptPath}");
        }

        return new XrayDnsIdentityScriptResult
        {
            ExitCode = proc.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString()
        };
    }
}
