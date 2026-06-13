using DataGateXRayManager.Hubs;
using DataGateMonitor.Serialization;

namespace DataGateXRayManager.Configurations;

public static class SignalRConfiguration
{
    public static void ConfigureSignalR(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<HubConnectionTracker>();
        services.AddSignalR()
            .AddNewtonsoftJsonProtocol(options => options.PayloadSerializerSettings = ProjectJson.WebSettings);
    }
}
