using System.Net;
using System.Security.Claims;
using DataGateXRayManager.Middlewares;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Moq;

namespace DataGateXRayManager.Tests.Middlewares;

public class JwtValidationMiddlewareTests
{
    private static HttpContext CreateContext(string path, IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remoteIp ?? IPAddress.Parse("203.0.113.10");
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/proxy")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Invoke_ExcludedPaths_AllowWithoutToken(string path)
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new JwtValidationMiddleware(next);

        await middleware.Invoke(CreateContext(path), Mock.Of<IMicroserviceJwtValidator>());

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_ApiInfo_FromInternetWithoutToken_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/info");

        await middleware.Invoke(context, Mock.Of<IMicroserviceJwtValidator>());

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_ApiInfo_FromLocalhostWithoutToken_Allows()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/info", IPAddress.Loopback);

        await middleware.Invoke(context, Mock.Of<IMicroserviceJwtValidator>());

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_ApiInfo_FromInternetWithValidToken_Allows()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/info");
        context.Request.Headers.Authorization = "Bearer good-token";

        var validator = new Mock<IMicroserviceJwtValidator>();
        validator
            .Setup(v => v.ValidateToken("good-token", out It.Ref<ClaimsPrincipal?>.IsAny, It.IsAny<JwtValidationRequestContext>()))
            .Returns((string _, out ClaimsPrincipal? principal, JwtValidationRequestContext _) =>
            {
                principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
                return true;
            });

        await middleware.Invoke(context, validator.Object);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_ProtectedPathWithoutToken_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/xray/clients");

        await middleware.Invoke(context, Mock.Of<IMicroserviceJwtValidator>());

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }
}
