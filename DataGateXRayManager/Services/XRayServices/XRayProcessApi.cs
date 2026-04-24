using System.Diagnostics;
using DataGateMonitor.SharedModels.DataGateXRayManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateXRayManager.Services.XRayServices;

/// <summary>
/// Invokes the Xray-core CLI (<c>xray api …</c>). See Xray-core
/// <c>main/commands/all/api/inbound_user_add.go</c>: <c>adu</c> reads config from file paths or the literal argument
/// <c>stdin:</c> (stdin body must be a JSON config document containing <c>inbounds</c> with the new user(s)).
/// <c>rmu</c> uses <c>-tag=…</c> and email positional arguments, not stdin.
/// </summary>
public class XRayProcessApi(
    IConfiguration configuration,
    IOptions<XRayManagementOptions> managementOptions,
    ILogger<XRayProcessApi> logger)
{
    private readonly XRayManagementOptions _api = managementOptions.Value;

    public string BinaryPath =>
        configuration["XRay:BinaryPath"]
        ?? "/usr/local/bin/xray";

    /// <summary>Backward-compatible: splits on spaces (no spaces inside individual args). Prefer <see cref="RunApiVerbAsync"/> for <c>rmu</c>.</summary>
    public Task<string> RunApiAsync(string apiSubcommandAndArgs, string? stdinBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiSubcommandAndArgs))
            throw new ArgumentException("API subcommand is required.", nameof(apiSubcommandAndArgs));

        var working = apiSubcommandAndArgs.Trim();
        var verb = working.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        if (verb.Equals("adu", StringComparison.OrdinalIgnoreCase) && stdinBody is not null
            && !working.Contains("stdin:", StringComparison.Ordinal))
            working = "adu stdin:";

        if (verb.Equals("rmu", StringComparison.OrdinalIgnoreCase) && stdinBody is not null)
        {
            logger.LogWarning(
                "xray api rmu does not read stdin; ignoring {Bytes} bytes of legacy stdin payload.",
                stdinBody.Length);
            stdinBody = null;
        }

        var segments = working.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        return RunApiCoreAsync(segments, stdinBody, cancellationToken);
    }

    /// <summary>Exact argv after <c>api</c> (e.g. <c>["adu","stdin:"]</c> or <c>["rmu","-tag=vless-in","user@mail"]</c>).</summary>
    public Task<string> RunApiVerbAsync(IReadOnlyList<string> verbAndArgs, string? stdinBody, CancellationToken cancellationToken)
    {
        if (verbAndArgs is null || verbAndArgs.Count == 0)
            throw new ArgumentException("At least one argument is required (e.g. adu or rmu).", nameof(verbAndArgs));

        var segments = verbAndArgs.ToList();
        var verb = segments[0];

        if (verb.Equals("adu", StringComparison.OrdinalIgnoreCase) && stdinBody is not null)
        {
            var hasStdinOrFileArg = segments.Skip(1).Any(s =>
                s.Equals("stdin:", StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            if (!hasStdinOrFileArg)
            {
                segments.Insert(1, "stdin:");
                logger.LogDebug("Inserted \"stdin:\" into xray api argv so core reads stdin JSON.");
            }
        }

        if (verb.Equals("rmu", StringComparison.OrdinalIgnoreCase) && stdinBody is not null)
        {
            logger.LogWarning(
                "xray api rmu does not read stdin; ignoring {Bytes} bytes of stdin payload.",
                stdinBody.Length);
            stdinBody = null;
        }

        return RunApiCoreAsync(segments, stdinBody, cancellationToken);
    }

    private static bool IsTransientXrayApiDialFailure(string stderr) =>
        stderr.Contains("failed to dial", StringComparison.OrdinalIgnoreCase)
        || stderr.Contains("connection refused", StringComparison.OrdinalIgnoreCase);

    private async Task<string> RunApiCoreAsync(List<string> segments, string? stdinBody, CancellationToken cancellationToken)
    {
        var host = string.IsNullOrWhiteSpace(_api.Host) ? "127.0.0.1" : _api.Host.Trim();
        var serverAddr = $"{host}:{_api.Port}";
        var verb = segments.Count > 0 ? segments[0] : "";

        const int maxAttempts = 12;
        const int delayMs = 400;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = BinaryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinBody is not null,
                CreateNoWindow = true
            };

            // xray-core parses `api <verb>` flags before positionals. If `-server` comes after `stdin:` or
            // emails, Go's flag package stops at the first non-flag and treats `-server=…` as a config path
            // (see inbound_user_add.go extractInboundsConfig / "failed to load %s").
            psi.ArgumentList.Add("api");
            psi.ArgumentList.Add(segments[0]);
            psi.ArgumentList.Add("-server=" + serverAddr);
            for (var i = 1; i < segments.Count; i++)
                psi.ArgumentList.Add(segments[i]);

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

            var combined = string.Join(Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (p.ExitCode == 0)
            {
                if (attempt > 1)
                    logger.LogInformation("xray api succeeded on attempt {Attempt} (server {Server}).", attempt, serverAddr);
                return FinishSuccessfulApiCallAsync(verb, combined, stdout, stderr);
            }

            logger.LogWarning("xray api exited {Code}. stderr: {Err}", p.ExitCode, stderr);
            var err = new InvalidOperationException(
                $"xray api failed ({p.ExitCode}): {stderr.Trim()} {stdout.Trim()}".Trim());
            lastError = err;

            if (attempt < maxAttempts && IsTransientXrayApiDialFailure(stderr))
            {
                logger.LogWarning(
                    "xray api dial to {Server} failed (attempt {Attempt}/{Max}); retrying after {Delay}ms.",
                    serverAddr, attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                continue;
            }

            throw err;
        }

        throw lastError ?? new InvalidOperationException("xray api failed with no error detail.");
    }

    private static string FinishSuccessfulApiCallAsync(string verb, string combined, string stdout, string stderr)
    {
        if (verb.Equals("adu", StringComparison.OrdinalIgnoreCase)
            && combined.Contains("Added 0 user", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "xray api adu reported 0 users added. Check stdin JSON (must include inbounds with VLESS clients and non-empty email) and inbound tag. Output: "
                + combined.Trim());
        }

        if (verb.Equals("rmu", StringComparison.OrdinalIgnoreCase)
            && combined.Contains("Removed 0 user", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "xray api rmu reported 0 users removed. Check -tag matches a running inbound and email matches the client. Output: "
                + combined.Trim());
        }

        var result = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        return string.IsNullOrWhiteSpace(result) ? combined.Trim() : result;
    }
}

