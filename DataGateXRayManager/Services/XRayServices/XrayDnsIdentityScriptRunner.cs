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

public sealed class ProcessXrayDnsIdentityScriptRunner : IXrayDnsIdentityScriptRunner
{
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
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new XrayDnsIdentityScriptResult
        {
            ExitCode = proc.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString()
        };
    }
}
