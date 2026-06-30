using System.Net;
using DataGateXRayManager.Services.Interfaces;

namespace DataGateXRayManager.Middlewares;

internal static class JwtValidationHttpContextExtensions
{
    internal static JwtValidationRequestContext ToJwtValidationRequestContext(this HttpContext context)
    {
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var method = string.IsNullOrWhiteSpace(context.Request.Method) ? "GET" : context.Request.Method;
        var userAgent = context.Request.Headers["User-Agent"].ToString();
        return new JwtValidationRequestContext(
            ResolveClientIp(context),
            path,
            method,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }

    private static string? ResolveClientIp(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',').Select(s => s.Trim()).FirstOrDefault();
            if (!string.IsNullOrEmpty(first) && IPAddress.TryParse(first, out _))
                return first;
        }

        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}

public class JwtValidationMiddleware(RequestDelegate next)
{
    private static readonly string[] ExcludedPaths =
    {
        "/", "/favicon.ico", "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/api/proxy"
    };

    private static readonly string[] LocalOnlyPaths =
    {
        "/api/vpn-events/connect",
        "/api/vpn-events/disconnect",
        "/api/vpn-events/tlsverify",
        "/api/vpn-events/attempt",
        "/api/vpn-events/envdump"
    };

    public async Task Invoke(HttpContext context, IMicroserviceJwtValidator validator)
    {
        var requestPath = context.Request.Path;
        var requestContext = context.ToJwtValidationRequestContext();

        if (ExcludedPaths.Any(p => requestPath.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (LocalOnlyPaths.Any(p => requestPath.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) &&
            (remoteIp is not null && (IPAddress.IsLoopback(remoteIp) || remoteIp.ToString() == "::1")))
        {
            await next(context);
            return;
        }

        string? token = null;
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            token = authHeader["Bearer ".Length..];

        if (string.IsNullOrWhiteSpace(token))
            token = context.Request.Query["access_token"];

        if (!string.IsNullOrWhiteSpace(token) && validator.ValidateToken(token, out var principal, requestContext))
        {
            context.User = principal ?? throw new InvalidOperationException("Principal is null");
            await next(context);
            return;
        }

        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
}
