using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DataGateXRayManager.Tests.Helpers;

public class VpnServerAnnounceApiUrlResolverTests
{
    [Fact]
    public void Resolve_PublicApiUrl_WinsOverPublicIpAndPort()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com:9443",
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com:9443/", result);
    }

    [Fact]
    public void Resolve_PublicApiUrl_PreservesTrailingSlash()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com/",
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com/", result);
    }

    [Fact]
    public void Resolve_WithoutPublicApiUrl_BuildsFromIpAndPort()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(null, "203.0.113.10", 5011);

        Assert.Equal("http://203.0.113.10:5011/", result);
    }

    [Fact]
    public void Resolve_WhitespacePublicApiUrl_FallsBackToIp()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve("   ", "198.51.100.2", 5010);

        Assert.Equal("http://198.51.100.2:5010/", result);
    }

    [Fact]
    public void Resolve_MissingIp_ReturnsNull()
    {
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, null, 5010));
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, "  ", 5010));
    }

    [Fact]
    public void ResolveApiPort_EnvWinsOverConfiguration()
    {
        var previous = Environment.GetEnvironmentVariable("API_PORT");
        try
        {
            Environment.SetEnvironmentVariable("API_PORT", "5099");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["API_PORT"] = "5012" })
                .Build();

            Assert.Equal(5099, VpnServerAnnounceApiUrlResolver.ResolveApiPort(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("API_PORT", previous);
        }
    }

    [Fact]
    public void ResolveApiPort_UsesConfigurationWhenEnvUnset()
    {
        var previous = Environment.GetEnvironmentVariable("API_PORT");
        try
        {
            Environment.SetEnvironmentVariable("API_PORT", null);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["API_PORT"] = "5012" })
                .Build();

            Assert.Equal(5012, VpnServerAnnounceApiUrlResolver.ResolveApiPort(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("API_PORT", previous);
        }
    }

    [Fact]
    public void GetConfiguredPublicApiUrl_EnvWinsOverConfiguration()
    {
        var previous = Environment.GetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey);
        try
        {
            Environment.SetEnvironmentVariable(
                VpnServerAnnounceApiUrlResolver.PublicApiUrlKey,
                "https://from-env.example/");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PUBLIC_API_URL"] = "https://from-config.example/"
                })
                .Build();

            Assert.Equal(
                "https://from-env.example/",
                VpnServerAnnounceApiUrlResolver.GetConfiguredPublicApiUrl(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey, previous);
        }
    }

    [Fact]
    public void GetConfiguredPublicIp_PrefersPublicIpEnv()
    {
        var previousPublicIp = Environment.GetEnvironmentVariable("PUBLIC_IP");
        var previousXrayIp = Environment.GetEnvironmentVariable("XRAY__IP");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", "81.27.109.193");
            Environment.SetEnvironmentVariable("XRAY__IP", "212.147.240.179");
            var config = new ConfigurationBuilder().Build();

            Assert.Equal("81.27.109.193", VpnServerAnnounceApiUrlResolver.GetConfiguredPublicIp(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", previousPublicIp);
            Environment.SetEnvironmentVariable("XRAY__IP", previousXrayIp);
        }
    }

    [Fact]
    public void GetConfiguredPublicIp_UsesXrayIpWhenPublicIpUnset()
    {
        var previousPublicIp = Environment.GetEnvironmentVariable("PUBLIC_IP");
        var previousXrayIp = Environment.GetEnvironmentVariable("XRAY__IP");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", null);
            Environment.SetEnvironmentVariable("XRAY__IP", "81.27.109.193");
            var config = new ConfigurationBuilder().Build();

            Assert.Equal("81.27.109.193", VpnServerAnnounceApiUrlResolver.GetConfiguredPublicIp(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", previousPublicIp);
            Environment.SetEnvironmentVariable("XRAY__IP", previousXrayIp);
        }
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_UsesConfiguredIpWithoutExternalLookup()
    {
        var previousPublicIp = Environment.GetEnvironmentVariable("PUBLIC_IP");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", "81.27.109.193");
            var config = new ConfigurationBuilder().Build();
            var external = new Mock<IExternalIpAddressService>();

            var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
                config, external.Object, CancellationToken.None);

            Assert.Equal("81.27.109.193", ip);
            external.Verify(
                x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", previousPublicIp);
        }
    }

    [Fact]
    public void GetConfiguredPublicApiUrl_BuildsFromXrayDomainWhenExplicitUnset()
    {
        var previousPublicApiUrl = Environment.GetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey);
        var previousDomain = Environment.GetEnvironmentVariable("XRAY__DOMAIN");
        try
        {
            Environment.SetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey, null);
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", null);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["XRAY:DOMAIN"] = "xs1-nor.datagateapp.com",
                    ["XRAY_API_HTTPS_PORT"] = "9443"
                })
                .Build();

            Assert.Equal(
                "https://xs1-nor.datagateapp.com:9443/",
                VpnServerAnnounceApiUrlResolver.GetConfiguredPublicApiUrl(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey, previousPublicApiUrl);
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", previousDomain);
        }
    }

    [Fact]
    public void GetConfiguredPublicApiUrl_ExplicitWinsOverXrayDomain()
    {
        var previousPublicApiUrl = Environment.GetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey);
        try
        {
            Environment.SetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey, null);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PUBLIC_API_URL"] = "https://custom.example:9443/",
                    ["XRAY:DOMAIN"] = "xs1-nor.datagateapp.com"
                })
                .Build();

            Assert.Equal(
                "https://custom.example:9443/",
                VpnServerAnnounceApiUrlResolver.GetConfiguredPublicApiUrl(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(VpnServerAnnounceApiUrlResolver.PublicApiUrlKey, previousPublicApiUrl);
        }
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_UnresolvableDomain_FallsBackToExternalLookup()
    {
        var previousPublicIp = Environment.GetEnvironmentVariable("PUBLIC_IP");
        var previousXrayIp = Environment.GetEnvironmentVariable("XRAY__IP");
        var previousDomain = Environment.GetEnvironmentVariable("XRAY__DOMAIN");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", null);
            Environment.SetEnvironmentVariable("XRAY__IP", null);
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", "this-hostname-does-not-exist.invalid");
            var config = new ConfigurationBuilder().Build();
            var external = new Mock<IExternalIpAddressService>();
            external
                .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("212.147.240.179");

            var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
                config, external.Object, CancellationToken.None);

            Assert.Equal("212.147.240.179", ip);
            external.Verify(
                x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", previousPublicIp);
            Environment.SetEnvironmentVariable("XRAY__IP", previousXrayIp);
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", previousDomain);
        }
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_FallsBackToExternalLookupWhenDomainUnset()
    {
        var previousPublicIp = Environment.GetEnvironmentVariable("PUBLIC_IP");
        var previousXrayIp = Environment.GetEnvironmentVariable("XRAY__IP");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", null);
            Environment.SetEnvironmentVariable("XRAY__IP", null);
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", null);
            var config = new ConfigurationBuilder().Build();
            var external = new Mock<IExternalIpAddressService>();
            external
                .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("212.147.240.179");

            var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
                config, external.Object, CancellationToken.None);

            Assert.Equal("212.147.240.179", ip);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_IP", previousPublicIp);
            Environment.SetEnvironmentVariable("XRAY__IP", previousXrayIp);
        }
    }
}
