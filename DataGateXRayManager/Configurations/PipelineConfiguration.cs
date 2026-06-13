using System.Reflection;
using DataGateXRayManager.Hubs;

namespace DataGateXRayManager.Configurations;

public static class PipelineConfiguration
{
    public static void ConfigurePipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "DataGateXRayManager API V1");
            });
        }

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });

        app.UseAuthorization();
        app.MapControllers();

        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.MapGet("/error/401", () => Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Authentication is required and has failed or has not yet been provided."))
            .ExcludeFromDescription();
        app.MapGet("/error/403", () => Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have permission to access this resource."))
            .ExcludeFromDescription();
        app.MapGet("/error/404", () => Results.Problem(statusCode: 404, title: "Page Not Found",
                detail: "The requested resource was not found."))
            .ExcludeFromDescription();
        app.MapGet("/", () => Results.Redirect("/api/info")).ExcludeFromDescription();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
        var environmentName = app.Environment.EnvironmentName;
        app.Logger.LogInformation("Application version: {Version}; Environment: {Env};", version, environmentName);

        app.MapHub<XRaySignalHub>("/hubs/xray");
        app.MapHub<XRayEventHub>("/hubs/xray-event");
    }
}
