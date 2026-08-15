using System.Net;
using DataGateXRayManager.Controllers;
using Microsoft.AspNetCore.Http;

namespace DataGateXRayManager.Tests.Controllers;

/// <summary>
/// Regression coverage for chat findings: spoofable XFF/clientRef and XFF+RemotePort mash.
/// </summary>
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

    [Fact]
    public void ResolveHttpClientAddress_TrustedXff_ForcesPortZero()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        ctx.Connection.RemotePort = 54321; // LB / docker hop — must not mash with XFF IP
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.44";

        var (ip, port) = XRayProxyController.ResolveHttpClientAddress(ctx);

        Assert.Equal("203.0.113.44", ip);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ResolveHttpClientAddress_DirectClient_KeepsRemotePort()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        ctx.Connection.RemotePort = 41234;

        var (ip, port) = XRayProxyController.ResolveHttpClientAddress(ctx);

        Assert.Equal("198.51.100.7", ip);
        Assert.Equal(41234, port);
    }

    [Fact]
    public void ResolveHttpClientAddress_SpoofedXffOnPublicPeer_KeepsConnectionPort()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        ctx.Connection.RemotePort = 41234;
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.99";

        var (ip, port) = XRayProxyController.ResolveHttpClientAddress(ctx);

        Assert.Equal("198.51.100.7", ip);
        Assert.Equal(41234, port);
    }

    [Theory]
    [InlineData("from-query", null, null, "from-query")]
    [InlineData(null, "from-client-ref", "from-common-name", "from-client-ref")]
    [InlineData(null, null, "from-common-name", "from-common-name")]
    [InlineData("  ", "  hdr  ", null, "hdr")]
    [InlineData(null, null, null, null)]
    public void ResolveCommonName_Priority_QueryThenClientRefThenCommonName(
        string? query,
        string? clientRefHeader,
        string? commonNameHeader,
        string? expected)
    {
        var ctx = new DefaultHttpContext();
        if (clientRefHeader is not null)
            ctx.Request.Headers["X-Client-Ref"] = clientRefHeader;
        if (commonNameHeader is not null)
            ctx.Request.Headers["X-Common-Name"] = commonNameHeader;

        Assert.Equal(expected, XRayProxyController.ResolveCommonName(query, ctx));
    }
}
