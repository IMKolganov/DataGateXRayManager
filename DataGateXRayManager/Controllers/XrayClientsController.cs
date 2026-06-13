using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.Interfaces;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

[ApiController]
[Route("api/xray")]
public sealed class XrayClientsController(
    IXRayActiveSessionsService activeSessionsService,
    IXRayUserService xRayUserService,
    IDataPathResolver dataPathResolver,
    ILogger<XrayClientsController> logger)
    : ControllerBase
{
    /// <summary>Dashboard backend polls this endpoint (see DataGateMonitor <c>XrayNodeApiClient</c>).</summary>
    [HttpGet("clients")]
    public async Task<ActionResult<ApiResponse<XrayClientsEnvelope>>> GetActiveClients(CancellationToken cancellationToken)
    {
        var result = await activeSessionsService.GetActiveClientsAsync(cancellationToken);
        return Ok(ApiResponse<XrayClientsEnvelope>.SuccessResponse(result));
    }

    /// <summary>
    /// Drops live sessions via <c>rmu</c>, then if the client still exists in <c>clients.store.json</c>, re-applies <c>adu</c>
    /// so the same profile can connect again (otherwise only <c>rmu</c> would leave the UUID unknown until restart/rehydrate).
    /// </summary>
    [HttpPost("clients/kick")]
    public async Task<ActionResult<ApiResponse<bool>>> KickUser([FromBody] XrayCommonNameRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CommonName))
            return BadRequest(ApiResponse<bool>.ErrorResponse("commonName is required"));
        try
        {
            await xRayUserService.KickInboundUserAsync(request.CommonName.Trim(), cancellationToken);
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kick user failed for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<bool>.ErrorResponse(ex.Message));
        }
    }

    /// <summary>Revokes the client in the local store and removes them from the inbound (same as client-link revoke).</summary>
    [HttpPost("users/disable")]
    public async Task<ActionResult<ApiResponse<bool>>> DisableUser([FromBody] XrayCommonNameRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CommonName))
            return BadRequest(ApiResponse<bool>.ErrorResponse("commonName is required"));
        try
        {
            var dataDir = dataPathResolver.GetDataPath();
            await xRayUserService.RevokeCertificateAsync(dataDir, request.CommonName.Trim(), cancellationToken);
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Disable user failed for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<bool>.ErrorResponse(ex.Message));
        }
    }
}
