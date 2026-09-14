using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Requests;
using DataGateMonitor.SharedModels.DataGateXRayManager.ClientLink.Responses;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class ClientLinkControllerTests
{
    private readonly Mock<IClientLinkService> _clientLinkService = new();
    private readonly Mock<IDataPathResolver> _pathResolver = new();
    private readonly Mock<ILogger<ClientLinkController>> _logger = new();

    private ClientLinkController CreateSut() =>
        new(_clientLinkService.Object, _pathResolver.Object, _logger.Object);

    [Fact]
    public async Task Add_WhenRequestValid_ReturnsOk()
    {
        var request = new GenerateClientLinkRequest
        {
            CommonName = "client1",
            ConfigTemplate = "vless://{{uuid}}@{{server_ip}}:{{server_port}}",
            FriendlyName = "Client 1",
            ServerIp = "1.2.3.4",
            ServerPort = 443
        };
        var metadata = new ClientLinkMetadata { CommonName = "client1", FileName = "client1.txt", FilePath = "/path/client1.txt" };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _clientLinkService.Setup(s => s.AddClientLink(
                "/data",
                "client1",
                "Client 1",
                request.ConfigTemplate,
                "1.2.3.4",
                443,
                It.IsAny<CancellationToken>(),
                request.IssuedTo,
                request.LinkExpireDays))
            .ReturnsAsync(metadata);

        var result = await CreateSut().Add(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ClientLinkMetadata>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("client1", response.Data!.CommonName);
    }

    [Fact]
    public async Task Add_WhenCommonNameEmpty_ReturnsBadRequest()
    {
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        var request = new GenerateClientLinkRequest { CommonName = "", ConfigTemplate = "template" };

        var result = await CreateSut().Add(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ClientLinkMetadata>>(bad.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task Revoke_WhenRequestValid_ReturnsOk()
    {
        var request = new RevokeClientLinkRequest
        {
            CommonName = "client1",
            FileName = "client1.txt",
            FilePath = "/path/client1.txt"
        };
        var metadata = new ClientLinkMetadata { CommonName = "client1", FilePath = "/revoked/client1.txt" };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _clientLinkService.Setup(s => s.RevokeClientLink(
                "/data", "client1", "client1.txt", "/path/client1.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var result = await CreateSut().Revoke(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ClientLinkMetadata>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("client1", response.Data!.CommonName);
    }

    [Fact]
    public async Task Revoke_WhenServiceThrows_ReturnsBadRequest()
    {
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _clientLinkService.Setup(s => s.RevokeClientLink(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("missing"));

        var result = await CreateSut().Revoke(new RevokeClientLinkRequest { CommonName = "cn" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<ClientLinkMetadata>>(bad.Value).Success);
    }

    [Fact]
    public async Task Download_WhenFileExists_ReturnsOkWithContent()
    {
        var request = new DownloadClientLinkRequest { FileName = "client1.txt", FilePath = "/path/client1.txt" };
        var download = new ClientLinkDownload { FileName = "client1.txt", Content = [1, 2, 3] };
        _clientLinkService.Setup(s => s.DownloadClientLink("client1.txt", "/path/client1.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);

        var result = await CreateSut().Download(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ClientLinkDownload>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(3, response.Data!.Content.Length);
    }

    [Fact]
    public async Task Download_WhenServiceThrows_ReturnsBadRequest()
    {
        _clientLinkService.Setup(s => s.DownloadClientLink(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("gone"));

        var result = await CreateSut().Download(
            new DownloadClientLinkRequest { FileName = "missing.txt" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<ClientLinkDownload>>(bad.Value).Success);
    }
}
