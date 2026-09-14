using DataGateMonitor.SharedModels.DataGateXRayManager.Info;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class IndexControllerTests
{
    private readonly Mock<IWebHostEnvironment> _env = new();
    private readonly Mock<ILogger<IndexController>> _logger = new();
    private readonly Mock<IExternalIpAddressService> _externalIp = new();

    public IndexControllerTests()
    {
        _env.Setup(e => e.EnvironmentName).Returns("Testing");
        _externalIp.Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    [Fact]
    public async Task Get_ReturnsOk_WithRootXrayInfoResponse()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DNS1"] = "8.8.8.8",
            ["DNS2"] = "8.8.4.4",
            ["VPN_SUBNET"] = "10.51.28.0",
            ["DATA_DIR"] = "/data",
            ["PORT"] = "443",
            ["API_PORT"] = "5011",
            ["PROTO"] = "tcp",
            ["XRayManagement:Host"] = "127.0.0.1",
            ["XRayManagement:Port"] = "10085",
            ["XRay:InboundTag"] = "vless-in",
            ["Backend:BaseUrl"] = "http://backend/"
        }).Build();
        _externalIp.Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("203.0.113.10");

        var controller = new IndexController(config, _env.Object, _logger.Object, _externalIp.Object);
        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RootXrayInfoResponse>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("DataGateXRayManager", response.Data!.Application);
        Assert.Equal("Testing", response.Data.Environment);
        Assert.Equal("203.0.113.10", response.Data.PublicIp);
        Assert.Equal("8.8.8.8", response.Data.Config!.Dns1);
        Assert.Equal("443", response.Data.Config.Port);
        Assert.Equal("10085", response.Data.Config.XRayManagement?.Port);
        Assert.Equal("vless-in", response.Data.Config.XRayManagement?.InboundTag);
    }

    [Fact]
    public async Task Get_WhenPublicIpLookupFails_StillReturnsOk_WithNullPublicIp()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        _externalIp.Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));

        var controller = new IndexController(config, _env.Object, _logger.Object, _externalIp.Object);
        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RootXrayInfoResponse>>(ok.Value);
        Assert.NotNull(response.Data!.Config);
        Assert.Null(response.Data.PublicIp);
    }
}
