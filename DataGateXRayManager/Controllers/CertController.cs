using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Requests;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

[ApiController]
[Route("api/certs")]
public class CertController(
    IXRayUserService xRayUserService,
    IDataPathResolver dataPathResolver,
    ILogger<CertController> logger)
    : ControllerBase
{
    [HttpGet("get-all")]
    public async Task<ActionResult<ApiResponse<List<ServerCertificate>>>> GetAllCertificates(CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = dataPathResolver.GetDataPath();

            var certificates = await xRayUserService.GetAllCertificateInfoInIndexFileAsync(
                mainPath,
                cancellationToken);

            return Ok(ApiResponse<List<ServerCertificate>>.SuccessResponse(certificates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all certificates");
            return BadRequest(ApiResponse<List<ServerCertificate>>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("add")]
    public async Task<ActionResult<ApiResponse<ServerCertificate>>> AddServerCertificate(
        [FromBody] AddServerCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = dataPathResolver.GetDataPath();

            if (request.CertExpireDays <= 0)
                request.CertExpireDays = 365;

            var result = await xRayUserService.BuildCertificateAsync(
                mainPath,
                cancellationToken,
                request.CommonName,
                request.CertExpireDays);

            return Ok(ApiResponse<ServerCertificate>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error building certificate for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<ServerCertificate>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<ApiResponse<ServerCertificate>>> RevokeCertificate([FromBody]
        RevokeServerCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = dataPathResolver.GetDataPath();

            var result = await xRayUserService.RevokeCertificateAsync(
                mainPath,
                request.CommonName,
                cancellationToken);

            return Ok(ApiResponse<ServerCertificate>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking certificate for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<ServerCertificate>.ErrorResponse(ex.Message));
        }
    }
}
