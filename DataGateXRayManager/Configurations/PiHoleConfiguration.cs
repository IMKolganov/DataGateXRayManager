using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Configurations;

public static class PiHoleConfiguration
{
    public static void ConfigurePiHole(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PiHoleOptions>(config.GetSection("PiHole"));
        services.PostConfigure<PiHoleOptions>(options => PiHoleEnvOverrides.ApplyLegacyEnv(config, options));

        services.AddSingleton<IPiHoleRuntimeOptionsStore, PiHoleRuntimeOptionsStore>();
        services.AddSingleton<IPiHoleCollectorStatusStore, PiHoleCollectorStatusStore>();

        services.AddHttpClient(PiHoleApiClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IPiHoleApiClient>(sp => new PiHoleApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(PiHoleApiClient.HttpClientName),
            sp.GetRequiredService<IPiHoleRuntimeOptionsStore>(),
            sp.GetRequiredService<ILogger<PiHoleApiClient>>()));

        services.AddSingleton<IPiHoleQueryCursorStore, PiHoleQueryCursorStore>();
        services.AddSingleton<IPiHoleClientIdentityResolver, PiHoleClientIdentityResolver>();
        services.AddHostedService<PiHoleQueryCollectorHostedService>();
    }
}
