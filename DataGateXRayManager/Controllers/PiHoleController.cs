using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;
using DataGateXRayManager.Services.XRayServices;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Diagnostics.Responses;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

[ApiController]
[Route("api/pi-hole")]
public class PiHoleController(
    IPiHoleRuntimeOptionsStore runtimeOptions,
    IPiHoleApiClient piHoleApiClient,
    IPiHoleCollectorStatusStore statusStore,
    IPiHoleQueryCursorStore cursorStore,
    IXrayDnsIdentitySyncService dnsIdentitySync,
    IConfiguration configuration,
    ILogger<PiHoleController> logger) : ControllerBase
{
    [HttpGet("config")]
    public ActionResult<ApiResponse<PiHoleOptionsDto>> GetConfig()
    {
        var options = runtimeOptions.GetEffective();
        return Ok(ApiResponse<PiHoleOptionsDto>.SuccessResponse(ToDto(options)));
    }

    [HttpPut("config")]
    public ActionResult<ApiResponse<PiHoleOptionsDto>> PutConfig([FromBody] PiHoleOptionsDto request)
    {
        var current = runtimeOptions.GetEffective();
        var merged = new PiHoleOptions
        {
            Enabled = request.Enabled,
            BaseUrl = request.BaseUrl?.Trim() ?? current.BaseUrl,
            AppPassword = string.IsNullOrWhiteSpace(request.AppPassword) || request.AppPassword == "********"
                ? current.AppPassword
                : request.AppPassword.Trim(),
            PollIntervalSeconds = request.PollIntervalSeconds > 0 ? request.PollIntervalSeconds : current.PollIntervalSeconds,
            BatchSize = request.BatchSize > 0 ? request.BatchSize : current.BatchSize,
            LookbackSeconds = request.LookbackSeconds >= 0 ? request.LookbackSeconds : current.LookbackSeconds,
            ClientSubnetPrefix = NormalizeClientSubnetPrefix(
                request.ClientSubnetPrefix?.Trim() ?? current.ClientSubnetPrefix),
            ClientSubnetExcludePrefixes = request.ClientSubnetExcludePrefixes?.Trim() ?? current.ClientSubnetExcludePrefixes
        };

        try
        {
            if (merged.Enabled && string.IsNullOrWhiteSpace(merged.BaseUrl))
                throw new InvalidOperationException("Pi-hole BaseUrl is required when the collector is enabled.");

            if (!string.IsNullOrWhiteSpace(merged.BaseUrl))
                PiHoleBaseUrlGuard.EnsureSafeOrThrow(merged.BaseUrl);

            if (dnsIdentitySync.IsEnabled)
                EnsureIdentityPrefixOrThrow(merged.ClientSubnetPrefix);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PiHoleOptionsDto>.ErrorResponse(ex.Message));
        }

        runtimeOptions.Apply(merged);
        statusStore.RecordConfigApplied(DateTimeOffset.UtcNow);
        logger.LogInformation(
            "Pi-hole runtime config applied. Enabled={Enabled}, BaseUrl={BaseUrl}, PollIntervalSec={PollIntervalSec}, BatchSize={BatchSize}, HasPassword={HasPassword}",
            merged.Enabled,
            merged.BaseUrl,
            merged.PollIntervalSeconds,
            merged.BatchSize,
            !string.IsNullOrEmpty(merged.AppPassword));

        return Ok(ApiResponse<PiHoleOptionsDto>.SuccessResponse(ToDto(merged)));
    }

    [HttpGet("diagnostics")]
    public async Task<ActionResult<ApiResponse<PiHoleDiagnosticsResponse>>> GetDiagnostics(
        CancellationToken cancellationToken)
    {
        try
        {
            var options = runtimeOptions.GetEffective();
            var probe = await piHoleApiClient.ProbeAsync(cancellationToken);
            var response = PiHoleDiagnosticsFactory.Create(
                options,
                statusStore.GetSnapshot(),
                cursorStore.GetLastUntilUtc(),
                probe,
                runtimeOptions.PersistedAppliedAtUtc);
            response.HealthMessage = dnsIdentitySync.IsEnabled
                ? "Xray node collector; CN via IdentityIp when client DNS uses Pi-hole through the tunnel."
                : "Xray node collector; enable XRAY_DNS_IDENTITY_* for per-user CN mapping via IdentityIp.";
            return Ok(ApiResponse<PiHoleDiagnosticsResponse>.SuccessResponse(response));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pi-hole diagnostics request failed.");
            return BadRequest(ApiResponse<PiHoleDiagnosticsResponse>.ErrorResponse(ex.Message));
        }
    }

    private void EnsureIdentityPrefixOrThrow(string? prefix)
    {
        var subnet = configuration["XRAY_DNS_IDENTITY_SUBNET"]
                     ?? configuration["Xray:DnsIdentity:Subnet"]
                     ?? XrayDnsIdentityAllocator.DefaultSubnetCidr;
        var suggested = XrayDnsIdentityAllocator.SuggestedClientSubnetPrefix(subnet);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException(
                "XRAY_DNS_IDENTITY_ENABLED requires ClientSubnetPrefix " +
                $"(suggested '{suggested ?? "10.80.0."}').");
        }

        if (!XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool(prefix, subnet))
        {
            throw new InvalidOperationException(
                $"ClientSubnetPrefix '{prefix}' does not cover identity subnet '{subnet}' " +
                $"(suggested '{suggested ?? "(n/a)"}').");
        }
    }

    /// <summary>
    /// Ensure trailing "." for IPv4 dotted prefixes so "10.80.0" matches identity hosts
    /// the same way as "10.80.0." and does not match "10.80.01.x".
    /// </summary>
    internal static string NormalizeClientSubnetPrefix(string? raw)
    {
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.EndsWith('.'))
            return trimmed;

        var looksLikeIpv4Prefix = true;
        foreach (var part in trimmed.Split('.'))
        {
            if (part.Length is 0 or > 3 || !part.All(char.IsDigit))
            {
                looksLikeIpv4Prefix = false;
                break;
            }
        }

        return looksLikeIpv4Prefix ? trimmed + "." : trimmed;
    }

    private static PiHoleOptionsDto ToDto(PiHoleOptions options) => new()
    {
        Enabled = options.Enabled,
        BaseUrl = options.BaseUrl,
        AppPassword = string.IsNullOrEmpty(options.AppPassword) ? string.Empty : "********",
        HasAppPassword = !string.IsNullOrEmpty(options.AppPassword),
        PollIntervalSeconds = options.PollIntervalSeconds,
        BatchSize = options.BatchSize,
        LookbackSeconds = options.LookbackSeconds,
        ClientSubnetPrefix = options.ClientSubnetPrefix,
        ClientSubnetExcludePrefixes = options.ClientSubnetExcludePrefixes
    };
}

public sealed class PiHoleOptionsDto
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string AppPassword { get; set; } = string.Empty;

    public bool HasAppPassword { get; set; }

    public int PollIntervalSeconds { get; set; }

    public int BatchSize { get; set; }

    public int LookbackSeconds { get; set; }

    public string ClientSubnetPrefix { get; set; } = string.Empty;

    public string ClientSubnetExcludePrefixes { get; set; } = string.Empty;
}
