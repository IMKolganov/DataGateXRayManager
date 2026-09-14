using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Enums;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Requests;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Responses;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Services.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class XRayProxyControllerTests
{
    private static XRayProxyController CreateController(IActiveProxyConnectionService active)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new XRayProxyController(
            config,
            Mock.Of<ILogger<XRayProxyController>>(),
            active,
            Mock.Of<IProxyConnectionHistoryService>());
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsNotFound_WhenNoConnection()
    {
        var controller = CreateController(new ActiveProxyConnectionService());

        var result = controller.GetClientByLocalPort(new GetProxyClientByLocalPortRequest
        {
            LocalPort = 65000,
            Host = "localhost"
        });

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(notFound.Value);
        Assert.False(body.Success);
        Assert.Contains("No active proxy session", body.Message);
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsBadRequest_WhenPortInvalid()
    {
        var controller = CreateController(new ActiveProxyConnectionService());

        var result = controller.GetClientByLocalPort(new GetProxyClientByLocalPortRequest { LocalPort = 0 });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(badRequest.Value).Success);
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsOk_WithConnection()
    {
        var active = new ActiveProxyConnectionService();
        active.Add(new ActiveProxyConnection
        {
            ConnectionId = "conn-1",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "192.0.2.10",
            RealClientPort = 48000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 41234,
            TargetIp = "127.0.0.1",
            TargetPort = 443,
            ConnectedAtUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
        });

        var result = CreateController(active).GetClientByLocalPort(new GetProxyClientByLocalPortRequest
        {
            LocalPort = 41234,
            Host = "localhost"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var wrapped = Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(ok.Value);
        Assert.True(wrapped.Success);
        Assert.Equal("conn-1", wrapped.Data!.ConnectionId);
        Assert.Equal("192.0.2.10", wrapped.Data.RealClientIp);
        Assert.Equal(41234, wrapped.Data.LocalProxyPort);
    }

    [Fact]
    public void ResolveClientIpFromContext_UsesRemoteIp_WhenPublic()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        ctx.Request.Headers["X-Forwarded-For"] = "198.51.100.1";

        var ip = XRayProxyController.ResolveClientIpFromContext(ctx);

        Assert.Equal("203.0.113.10", ip);
    }

    [Fact]
    public void ResolveClientIpFromContext_UsesForwardedFor_WhenPeerIsLoopback()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        ctx.Request.Headers["X-Forwarded-For"] = "198.51.100.7, 10.0.0.1";

        var ip = XRayProxyController.ResolveClientIpFromContext(ctx);

        Assert.Equal("198.51.100.7", ip);
    }
}
