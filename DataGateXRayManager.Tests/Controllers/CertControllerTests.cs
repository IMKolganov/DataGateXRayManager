using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Requests;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class CertControllerTests
{
    private readonly Mock<IXRayUserService> _userService = new();
    private readonly Mock<IDataPathResolver> _pathResolver = new();
    private readonly Mock<ILogger<CertController>> _logger = new();

    private CertController CreateSut() =>
        new(_userService.Object, _pathResolver.Object, _logger.Object);

    [Fact]
    public async Task GetAllCertificates_WhenPathAndServiceOk_ReturnsOkWithList()
    {
        var certs = new List<ServerCertificate> { new() { CommonName = "client1", SerialNumber = "01" } };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.GetAllCertificateInfoInIndexFileAsync("/data", It.IsAny<CancellationToken>()))
            .ReturnsAsync(certs);

        var result = await CreateSut().GetAllCertificates(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var wrapped = Assert.IsType<ApiResponse<List<ServerCertificate>>>(ok.Value);
        Assert.True(wrapped.Success);
        Assert.NotNull(wrapped.Data);
        Assert.Single(wrapped.Data);
        Assert.Equal("client1", wrapped.Data[0].CommonName);
    }

    [Fact]
    public async Task GetAllCertificates_WhenServiceThrows_ReturnsBadRequest()
    {
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("store not found"));

        var result = await CreateSut().GetAllCertificates(CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<List<ServerCertificate>>>(bad.Value).Success);
    }

    [Fact]
    public async Task AddServerCertificate_WhenRequestValid_ReturnsOkWithCertificate()
    {
        var request = new AddServerCertificateRequest { CommonName = "newclient", CertExpireDays = 365 };
        var cert = new ServerCertificate { CommonName = "newclient" };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.BuildCertificateAsync("/data", It.IsAny<CancellationToken>(), "newclient", 365))
            .ReturnsAsync(cert);

        var result = await CreateSut().AddServerCertificate(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var wrapped = Assert.IsType<ApiResponse<ServerCertificate>>(ok.Value);
        Assert.True(wrapped.Success);
        Assert.Equal("newclient", wrapped.Data!.CommonName);
    }

    [Fact]
    public async Task AddServerCertificate_WhenExpireDaysNonPositive_DefaultsTo365()
    {
        var request = new AddServerCertificateRequest { CommonName = "newclient", CertExpireDays = 0 };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.BuildCertificateAsync("/data", It.IsAny<CancellationToken>(), "newclient", 365))
            .ReturnsAsync(new ServerCertificate { CommonName = "newclient" });

        var result = await CreateSut().AddServerCertificate(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(365, request.CertExpireDays);
        _userService.Verify(
            s => s.BuildCertificateAsync("/data", It.IsAny<CancellationToken>(), "newclient", 365),
            Times.Once);
    }

    [Fact]
    public async Task RevokeCertificate_WhenRequestValid_ReturnsOk()
    {
        var request = new RevokeServerCertificateRequest { CommonName = "client1" };
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.RevokeCertificateAsync("/data", "client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerCertificate { CommonName = "client1", IsRevoked = true });

        var result = await CreateSut().RevokeCertificate(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var wrapped = Assert.IsType<ApiResponse<ServerCertificate>>(ok.Value);
        Assert.True(wrapped.Success);
        Assert.True(wrapped.Data!.IsRevoked);
    }

    [Fact]
    public async Task RevokeCertificate_WhenServiceThrows_ReturnsBadRequest()
    {
        _pathResolver.Setup(p => p.GetDataPath()).Returns("/data");
        _userService.Setup(s => s.RevokeCertificateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var result = await CreateSut().RevokeCertificate(
            new RevokeServerCertificateRequest { CommonName = "missing" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
