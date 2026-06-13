using System.Threading.RateLimiting;
using DataGateXRayManager.Helpers;
using DataGateMonitor.SharedModels.DataGateXRayManager.Models;
using DataGateXRayManager.Services;
using DataGateXRayManager.Services.Interfaces;
using DataGateXRayManager.Services.Proxy;
using DataGateXRayManager.Services.XRayServices;
using DataGateXRayManager.Services.XRayTelnet;
namespace DataGateXRayManager.Configurations;

public static class ServiceConfiguration
{
    public static void ConfigureServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IClientLinkService, ClientLinkService>();

        services.AddScoped<IXRayUserService, XRayUserService>();
        services.AddScoped<IXRayActiveSessionsService, XRayActiveSessionsService>();

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User?.Identity?.Name ?? context.Request.Headers.Host.ToString(),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });

        services.AddSingleton<IDataPathResolver, DataPathResolver>();

        services.AddHttpClient<MicroserviceJwtValidator>(client =>
        {
            var baseUrl = config["Backend:BaseUrl"];
            client.BaseAddress = new Uri(baseUrl ?? throw new InvalidOperationException("Backend:BaseUrl is required"));
        });

        services.AddSingleton<IMicroserviceJwtValidator>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MicroserviceJwtValidator));
            var logger = sp.GetRequiredService<ILogger<MicroserviceJwtValidator>>();
            return new MicroserviceJwtValidator(client, logger);
        });

        services.AddHostedService<MicroserviceJwtValidatorInitializer>();
        services.AddHostedService<XrayStoreRehydrateHostedService>();

        services.Configure<XRayManagementOptions>(config.GetSection("XRayManagement"));
        services.AddSingleton<XRayProcessApi>();
        services.AddSingleton<IXRayProcessApiRunner>(sp => sp.GetRequiredService<XRayProcessApi>());
        services.AddSingleton<XRayCoreApiCapabilities>();
        services.AddSingleton<XRayManagementSignalService>();
        services.AddHostedService<XRayAccessLogTailHostedService>();

        services.ConfigureProxy(config);

        services.AddControllers().AddNewtonsoftJson();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }
}
