using DataGateXRayManager.Middlewares;

namespace DataGateXRayManager.Configurations;

public static class MiddlewareConfiguration
{
    public static void ConfigureMiddleware(this WebApplication app)
    {
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseMiddleware<JwtValidationMiddleware>();
    }
}
