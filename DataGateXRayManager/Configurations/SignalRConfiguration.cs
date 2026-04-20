using DataGateXRayManager.Hubs;

namespace DataGateXRayManager.Configurations;

public static class SignalRConfiguration
{
    public static void ConfigureSignalR(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<HubConnectionTracker>();
        services.AddSignalR();
    }
}
