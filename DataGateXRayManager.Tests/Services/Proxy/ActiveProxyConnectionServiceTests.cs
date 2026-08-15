using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Enums;
using DataGateXRayManager.Controllers;
using DataGateXRayManager.Services.Proxy;
using Microsoft.AspNetCore.Http;

namespace DataGateXRayManager.Tests.Services.Proxy;

public class ActiveProxyConnectionServiceTests
{
    [Fact]
    public void Add_WithCommonName_StoresAndClearsOnRemove()
    {
        var sut = new ActiveProxyConnectionService();
        var conn = new ActiveProxyConnection
        {
            ConnectionId = "abc",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "1.2.3.4",
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 1,
            ConnectedAtUtc = DateTime.UtcNow
        };

        sut.Add(conn, "  my-cn  ");
        Assert.Equal(1, sut.Count);
        Assert.Equal("my-cn", sut.GetCommonName("abc"));
        Assert.Single(sut.GetAllWithCommonNames());
        Assert.Equal("my-cn", sut.GetAllWithCommonNames()[0].CommonName);

        Assert.True(sut.Remove("abc"));
        Assert.Equal(0, sut.Count);
        Assert.Null(sut.GetCommonName("abc"));
    }

    [Fact]
    public void Add_WithoutCommonName_OverwritesPrevious()
    {
        var sut = new ActiveProxyConnectionService();
        var conn = new ActiveProxyConnection
        {
            ConnectionId = "abc",
            Protocol = ProxyConnectionProtocol.Tcp,
            ConnectedAtUtc = DateTime.UtcNow
        };

        sut.Add(conn, "cn");
        sut.Add(conn, null);
        Assert.Null(sut.GetCommonName("abc"));
    }

    [Fact]
    public void TryGetByLocalProxy_FindsByNormalizedLoopback()
    {
        var sut = new ActiveProxyConnectionService();
        sut.Add(new ActiveProxyConnection
        {
            ConnectionId = "1",
            Protocol = ProxyConnectionProtocol.Tcp,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 555,
            ConnectedAtUtc = DateTime.UtcNow
        });

        var found = sut.TryGetByLocalProxy(555, "localhost");
        Assert.NotNull(found);
        Assert.Equal("1", found!.ConnectionId);
    }
}

public class XRayProxyControllerResolveCommonNameTests
{
    [Fact]
    public void ResolveCommonName_PrefersQuery()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Client-Ref"] = "from-header";
        ctx.Request.Headers["X-Common-Name"] = "from-cn-header";

        var result = XRayProxyController.ResolveCommonName("from-query", ctx);
        Assert.Equal("from-query", result);
    }

    [Fact]
    public void ResolveCommonName_FallsBackToClientRefHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Client-Ref"] = "hdr-ref";
        ctx.Request.Headers["X-Common-Name"] = "hdr-cn";

        Assert.Equal("hdr-ref", XRayProxyController.ResolveCommonName(null, ctx));
    }

    [Fact]
    public void ResolveCommonName_FallsBackToCommonNameHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Common-Name"] = "only-cn";

        Assert.Equal("only-cn", XRayProxyController.ResolveCommonName("  ", ctx));
    }

    [Fact]
    public void ResolveCommonName_Empty_ReturnsNull()
    {
        Assert.Null(XRayProxyController.ResolveCommonName(null, new DefaultHttpContext()));
    }
}
