using DataGateXRayManager.Services.Proxy;

namespace DataGateXRayManager.Configurations;

public static class ProxyConfiguration
{
    public static void ConfigureProxy(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IProxyConnectionHistoryService, ProxyConnectionHistoryService>();
        services.AddSingleton<IActiveProxyConnectionService, ActiveProxyConnectionService>();
    }
}
