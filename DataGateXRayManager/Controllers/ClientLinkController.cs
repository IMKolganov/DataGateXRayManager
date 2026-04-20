using DataGateXRayManager.Helpers;
using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Requests;
using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

[ApiController]
[Route("api/client-links")]
public class ClientLinkController(
    IClientLinkService clientLinkService,
    IDataPathResolver dataPathResolver,
    ILogger<ClientLinkController> logger)
    : ControllerBase
{
    [HttpPost("add")]
    public async Task<ActionResult<ClientLinkMetadata>> Add([FromBody] GenerateClientLinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = dataPathResolver.GetDataPath();

            if (string.IsNullOrEmpty(request.CommonName) || string.IsNullOrEmpty(request.ConfigTemplate))
                throw new NullReferenceException("Common name and config template are required");

            var result = await clientLinkService.AddClientLink(
                mainPath,
                request.CommonName,
                request.FriendlyName,
                request.ConfigTemplate,
                request.ServerIp,
                request.ServerPort,
                cancellationToken,
                request.IssuedTo,
                request.LinkExpireDays);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding client link file for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<ClientLinkMetadata>> Revoke([FromBody] RevokeClientLinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = dataPathResolver.GetDataPath();

            var result = await clientLinkService.RevokeClientLink(
                mainPath,
                request.CommonName,
                request.FileName,
                request.FilePath,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking client link for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("download")]
    public async Task<ActionResult<ClientLinkDownload>> Download([FromBody] DownloadClientLinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await clientLinkService.DownloadClientLink(
                request.FileName,
                request.FilePath,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading client link for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}
