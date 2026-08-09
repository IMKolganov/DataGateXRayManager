using System.Reflection;
using DataGateMonitor.SharedModels.DataGateXRayManager.Info;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateXRayManager.Controllers;

[ApiController]
[Route("api/info")]
public class IndexController(
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<IndexController> logger,
    IExternalIpAddressService externalIpAddressService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<RootXrayInfoResponse>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
            string? publicIp = null;
            try
            {
                publicIp = await externalIpAddressService.GetPublicIpAddressAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve PublicIp for /api/info");
            }

            var response = new RootXrayInfoResponse
            {
                Version = version,
                Environment = env.EnvironmentName,
                Application = "DataGateXRayManager",
                Description =
                    "Manages XRay (VLESS) clients: certificates, client links, WebSocket proxy. Events: POST /api/vpn-events/* (hooks), SignalR /hubs/xray-event (ClientConnected/Disconnected, AccessLogRecord from access.log JSON, AccessLogRaw fallback).",
                PublicIp = publicIp,
                Config = new ConfigInfoResponse
                {
                    Dns1 = config["DNS1"],
                    Dns2 = config["DNS2"],
                    VpnSubnet = config["VPN_SUBNET"],
                    VpnNetmask = config["VPN_NETMASK"],
                    DataDir = config["DATA_DIR"],
                    Port = config["PORT"],
                    ApiPort = config["API_PORT"],
                    Proto = config["PROTO"],
                    XRayManagement = new XRayManagementInfoResponse
                    {
                        Host = config["XRayManagement:Host"],
                        Port = config["XRayManagement:Port"],
                        InboundTag = config["XRay:InboundTag"]
                    },
                    BackendBaseUrl = config["Backend:BaseUrl"]
                }
            };
            return Ok(ApiResponse<RootXrayInfoResponse>.SuccessResponse(response));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting info");
            return BadRequest(ApiResponse<RootXrayInfoResponse>.ErrorResponse(ex.Message));
        }
    }
}
