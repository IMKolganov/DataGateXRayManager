using System.Diagnostics;
using DataGateXRayManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateXRayManager.Services.XRayServices;

public class XRayProcessApi(
    IConfiguration configuration,
    IOptions<XRayManagementOptions> managementOptions,
    ILogger<XRayProcessApi> logger)
{
    private readonly XRayManagementOptions _api = managementOptions.Value;

    public string BinaryPath =>
        configuration["XRay:BinaryPath"]
        ?? "/usr/local/bin/xray";

    public async Task<string> RunApiAsync(string apiSubcommandAndArgs, string? stdinBody, CancellationToken cancellationToken)
    {
        var server = $"{_api.Host}:{_api.Port}";
        var psi = new ProcessStartInfo
        {
            FileName = BinaryPath,
            Arguments = $"api {apiSubcommandAndArgs} -server={server}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinBody is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        if (stdinBody is not null)
        {
            await p.StandardInput.WriteAsync(stdinBody.AsMemory(), cancellationToken);
            await p.StandardInput.FlushAsync(cancellationToken);
            p.StandardInput.Close();
        }

        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);

        if (p.ExitCode != 0)
        {
            logger.LogWarning("xray api exited {Code}. stderr: {Err}", p.ExitCode, stderr);
            throw new InvalidOperationException($"xray api failed ({p.ExitCode}): {stderr.Trim()} {stdout.Trim()}".Trim());
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }
}
