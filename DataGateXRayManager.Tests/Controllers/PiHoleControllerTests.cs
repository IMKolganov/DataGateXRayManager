using DataGateMonitor.SharedModels.Responses;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;
using DataGateXRayManager.Services.XRayServices;
using DataGateXRayManager.Tests.Services.PiHole;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Controllers;

public class PiHoleControllerTests
{
    [Fact]
    public void PutConfig_NormalizesUndottedClientSubnetPrefix()
    {
        var store = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = false,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "secret"
        });
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(false);

        var controller = CreateController(store, sync.Object);
        var result = controller.PutConfig(new PiHoleOptionsDto
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "********",
            PollIntervalSeconds = 60,
            BatchSize = 200,
            LookbackSeconds = 120,
            ClientSubnetPrefix = "10.80.0"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PiHoleOptionsDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal("10.80.0.", envelope.Data!.ClientSubnetPrefix);
        Assert.Equal("10.80.0.", store.GetEffective().ClientSubnetPrefix);
    }

    [Fact]
    public void PutConfig_IdentityOn_AcceptsUndottedPrefixCoveringPool()
    {
        var store = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "secret",
            ClientSubnetPrefix = "10.80.0."
        });
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XRAY_DNS_IDENTITY_SUBNET"] = "10.80.0.0/24"
        }).Build();

        var controller = CreateController(store, sync.Object, config);
        var result = controller.PutConfig(new PiHoleOptionsDto
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "********",
            PollIntervalSeconds = 60,
            BatchSize = 200,
            LookbackSeconds = 120,
            ClientSubnetPrefix = "10.80.0"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<PiHoleOptionsDto>>(ok.Value).Success);
        Assert.Equal("10.80.0.", store.GetEffective().ClientSubnetPrefix);
    }

    [Fact]
    public void PutConfig_IdentityOn_RejectsEmptyPrefix()
    {
        var store = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "secret"
        });
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XRAY_DNS_IDENTITY_SUBNET"] = "10.80.0.0/24"
        }).Build();

        var controller = CreateController(store, sync.Object, config);
        var result = controller.PutConfig(new PiHoleOptionsDto
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "********",
            PollIntervalSeconds = 60,
            BatchSize = 200,
            LookbackSeconds = 120,
            ClientSubnetPrefix = ""
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PiHoleOptionsDto>>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Contains("ClientSubnetPrefix", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PutConfig_IdentityOn_RejectsMisalignedPrefix()
    {
        var store = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "secret"
        });
        var sync = new Mock<IXrayDnsIdentitySyncService>();
        sync.SetupGet(x => x.IsEnabled).Returns(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XRAY_DNS_IDENTITY_SUBNET"] = "10.80.0.0/24"
        }).Build();

        var controller = CreateController(store, sync.Object, config);
        var result = controller.PutConfig(new PiHoleOptionsDto
        {
            Enabled = true,
            BaseUrl = "http://172.20.0.1:8080",
            AppPassword = "********",
            PollIntervalSeconds = 60,
            BatchSize = 200,
            LookbackSeconds = 120,
            ClientSubnetPrefix = "10.80.1."
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static PiHoleController CreateController(
        IPiHoleRuntimeOptionsStore store,
        IXrayDnsIdentitySyncService sync,
        IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new PiHoleController(
            store,
            Mock.Of<IPiHoleApiClient>(),
            new PiHoleCollectorStatusStore(),
            Mock.Of<IPiHoleQueryCursorStore>(),
            sync,
            configuration,
            NullLogger<PiHoleController>.Instance);
    }
}
