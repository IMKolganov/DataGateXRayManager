using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Models;
using DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent.Requests;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class VpnEventControllerTests
{
    private readonly Mock<ILogger<VpnEventController>> _logger = new();

    private static IHubContext<XRayEventHub> CreateHubContext(IClientProxy clientProxy)
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(clientProxy);
        var hubContext = new Mock<IHubContext<XRayEventHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
        return hubContext.Object;
    }

    private VpnEventController CreateSut(IClientProxy? clientProxy = null)
    {
        clientProxy ??= Mock.Of<IClientProxy>(c =>
            c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()) ==
            Task.CompletedTask);
        return new VpnEventController(_logger.Object, CreateHubContext(clientProxy));
    }

    [Fact]
    public async Task OnClientConnect_ReturnsOk()
    {
        var result = await CreateSut().OnClientConnect(
            new VpnEventRequest { CommonName = "client1", RealAddress = "10.0.0.1" },
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnClientDisconnect_ReturnsOk()
    {
        var result = await CreateSut().OnClientDisconnect(
            new VpnEventRequest { CommonName = "client1", DurationSec = 120 },
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnClientAttempt_ReturnsOk()
    {
        var result = await CreateSut().OnClientAttempt(
            new VpnEventRequest { CommonName = "client1", VirtualAddress = "10.51.16.2" },
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnTlsVerify_ReturnsOk()
    {
        var result = await CreateSut().OnTlsVerify(
            new VpnEventRequest { CommonName = "client1", Message = "ok" },
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnError_ReturnsOk_AndSendsErrorEventAndAuthFailed()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut(clientProxy.Object).OnError(
            new VpnEventRequest { CommonName = "client1", Message = "Auth failed", EventType = "AuthFailed" },
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
        clientProxy.Verify(
            c => c.SendCoreAsync("ErrorEvent", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        clientProxy.Verify(
            c => c.SendCoreAsync("AuthFailed", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void OnEnvDump_ReturnsOk()
    {
        var result = CreateSut().OnEnvDump(new VpnEnvDump { Hook = "connect" });

        Assert.IsType<OkResult>(result);
    }
}
