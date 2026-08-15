using System.Net;
using DataGateXRayManager.Controllers;
using Microsoft.AspNetCore.Http;

namespace DataGateXRayManager.Tests.Controllers;

public class XRayProxyControllerResolveClientIpTests
{
    [Fact]
    public void ResolveClientIp_PrefersPublicXff_WhenTcpPeerIsPrivate()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.9, 10.0.0.1";

        Assert.Equal("203.0.113.9", XRayProxyController.ResolveClientIpFromContext(ctx));
    }

    [Fact]
    public void ResolveClientIp_IgnoresXff_WhenTcpPeerIsPublic()
    {
        // Unauthenticated /api/proxy: anyone can send XFF; only trust it behind a private edge.
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        Assert.Equal("198.51.100.1", XRayProxyController.ResolveClientIpFromContext(ctx));
    }

    [Fact]
    public void ResolveClientIp_IgnoresPrivateXff_UsesConnection()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        ctx.Request.Headers["X-Forwarded-For"] = "172.16.0.9";

        Assert.Equal("10.0.0.1", XRayProxyController.ResolveClientIpFromContext(ctx));
    }

    [Fact]
    public void ResolveClientIp_IgnoresInvalidXForwardedFor_UsesConnection()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx.Request.Headers["X-Forwarded-For"] = "not-an-ip";

        Assert.Equal("198.51.100.1", XRayProxyController.ResolveClientIpFromContext(ctx));
    }

    [Fact]
    public void ResolveClientIp_WithoutHeader_UsesConnectionRemote()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.55");

        Assert.Equal("192.0.2.55", XRayProxyController.ResolveClientIpFromContext(ctx));
    }
}
