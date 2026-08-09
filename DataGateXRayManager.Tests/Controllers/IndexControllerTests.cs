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
        _externalIp
            .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    [Fact]
    public async Task Get_ReturnsOk_WithPublicIp()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PORT"] = "443",
            ["API_PORT"] = "5010",
            ["Backend:BaseUrl"] = "http://backend/"
        }).Build();
        _externalIp
            .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("203.0.113.20");

        var controller = new IndexController(config, _env.Object, _logger.Object, _externalIp.Object);
        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RootXrayInfoResponse>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("DataGateXRayManager", response.Data!.Application);
        Assert.Equal("203.0.113.20", response.Data.PublicIp);
        Assert.Equal("443", response.Data.Config.Port);
    }

    [Fact]
    public async Task Get_WhenPublicIpLookupFails_StillReturnsOk_WithNullPublicIp()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        _externalIp
            .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));

        var controller = new IndexController(config, _env.Object, _logger.Object, _externalIp.Object);
        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RootXrayInfoResponse>>(ok.Value);
        Assert.Null(response.Data!.PublicIp);
        Assert.NotNull(response.Data.Config);
    }
}
