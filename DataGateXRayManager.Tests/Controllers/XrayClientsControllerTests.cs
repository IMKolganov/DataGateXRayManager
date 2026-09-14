using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class XrayClientsControllerTests
{
    private readonly Mock<IXRayActiveSessionsService> _sessions = new();
    private readonly Mock<IXRayUserService> _users = new();
    private readonly Mock<IDataPathResolver> _paths = new();
    private readonly Mock<ILogger<XrayClientsController>> _logger = new();

    private XrayClientsController CreateSut() =>
        new(_sessions.Object, _users.Object, _paths.Object, _logger.Object);

    [Fact]
    public async Task GetActiveClients_ReturnsOkEnvelope()
    {
        var envelope = new XrayClientsEnvelope
        {
            Clients = [new XrayClientSessionDto { Email = "cn1", RemoteAddress = "1.2.3.4" }],
            PolledAt = DateTimeOffset.UtcNow
        };
        _sessions.Setup(s => s.GetActiveClientsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(envelope);

        var result = await CreateSut().GetActiveClients(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<XrayClientsEnvelope>>(ok.Value);
        Assert.True(response.Success);
        Assert.Single(response.Data!.Clients);
        Assert.Equal("cn1", response.Data.Clients[0].Email);
    }

    [Fact]
    public async Task KickUser_WhenCommonNameMissing_ReturnsBadRequest()
    {
        var result = await CreateSut().KickUser(new XrayCommonNameRequest { CommonName = "  " }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<bool>>(bad.Value).Success);
        _users.Verify(s => s.KickInboundUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KickUser_WhenRequestValid_ReturnsOk()
    {
        _users.Setup(s => s.KickInboundUserAsync("client1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateSut().KickUser(new XrayCommonNameRequest { CommonName = " client1 " }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<bool>>(ok.Value).Success);
        _users.Verify(s => s.KickInboundUserAsync("client1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickUser_WhenServiceThrows_ReturnsBadRequest()
    {
        _users.Setup(s => s.KickInboundUserAsync("client1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rmu failed"));

        var result = await CreateSut().KickUser(new XrayCommonNameRequest { CommonName = "client1" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task DisableUser_WhenCommonNameMissing_ReturnsBadRequest()
    {
        var result = await CreateSut().DisableUser(new XrayCommonNameRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _users.Verify(
            s => s.RevokeCertificateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisableUser_WhenRequestValid_RevokesFromDataDir()
    {
        _paths.Setup(p => p.GetDataPath()).Returns("/data");
        _users.Setup(s => s.RevokeCertificateAsync("/data", "client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses.ServerCertificate
            {
                CommonName = "client1",
                IsRevoked = true
            });

        var result = await CreateSut().DisableUser(new XrayCommonNameRequest { CommonName = "client1" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<bool>>(ok.Value).Success);
        _users.Verify(s => s.RevokeCertificateAsync("/data", "client1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
