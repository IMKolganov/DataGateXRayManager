using DataGateXRayManager.Configurations;
using DataGateXRayManager.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateXRayManager.Tests.Configurations;

public class ServiceConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://files.example/backend")]
    public void ConfigureServices_UnusableBackendBaseUrl_DoesNotFailStartup(string? raw)
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?>();
        if (raw is not null)
            configData["Backend:BaseUrl"] = raw;
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        services.ConfigureServices(config);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Null(factory.CreateClient(nameof(MicroserviceJwtValidator)).BaseAddress);
        Assert.Null(factory.CreateClient(VpnServerAnnounceHostedService.HttpClientName).BaseAddress);
    }

    [Fact]
    public void ConfigureServices_BackendBaseUrl_NormalizesTrailingSlash()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:9999" }).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        services.ConfigureServices(config);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MicroserviceJwtValidator));

        Assert.Equal(new Uri("http://localhost:9999/"), client.BaseAddress);
    }

    [Fact]
    public async Task InitAsync_WithoutBackendBaseUrl_ReturnsInsteadOfBlockingStartup()
    {
        var validator = new MicroserviceJwtValidator(new HttpClient(), NullLogger<MicroserviceJwtValidator>.Instance);
        var init = validator.InitAsync();
        var winner = await Task.WhenAny(init, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(init, winner);
        await init;
    }
}
