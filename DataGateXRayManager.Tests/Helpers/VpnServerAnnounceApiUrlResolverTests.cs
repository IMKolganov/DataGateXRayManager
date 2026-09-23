using DataGateXRayManager.Helpers;
using Microsoft.Extensions.Configuration;

namespace DataGateXRayManager.Tests.Helpers;

public class VpnServerAnnounceApiUrlResolverTests
{
    [Fact]
    public void Resolve_PublicApiUrl_WinsOverDomainAndIp()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com:9443",
            "xray.example.com",
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com:9443/", result);
    }

    [Fact]
    public void Resolve_PublicApiUrl_PreservesTrailingSlash()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com/",
            null,
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com/", result);
    }

    [Fact]
    public void Resolve_WithoutPublicApiUrl_PrefersDomainOverIp()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            null,
            "xray.example.com",
            "203.0.113.10",
            9443);

        Assert.Equal("https://xray.example.com:9443/", result);
    }

    [Fact]
    public void Resolve_WithoutPublicApiUrlOrDomain_BuildsFromIpAndPort()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(null, null, "203.0.113.10", 5011);

        Assert.Equal("http://203.0.113.10:5011/", result);
    }

    [Fact]
    public void Resolve_WhitespacePublicApiUrl_FallsBackToDomain()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "   ",
            "node.example.com",
            "198.51.100.2",
            5010);

        Assert.Equal("https://node.example.com:5010/", result);
    }

    [Fact]
    public void Resolve_MissingDomainAndIp_ReturnsNull()
    {
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, null, null, 5010));
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, "  ", "  ", 5010));
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
    public void GetConfiguredDomain_PrefersXrayDoubleUnderscoreEnv()
    {
        var prevDd = Environment.GetEnvironmentVariable("XRAY__DOMAIN");
        var prevSingle = Environment.GetEnvironmentVariable("XRAY_DOMAIN");
        try
        {
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", "from-dd.example.com");
            Environment.SetEnvironmentVariable("XRAY_DOMAIN", "from-single.example.com");
            var config = new ConfigurationBuilder().Build();

            Assert.Equal("from-dd.example.com", VpnServerAnnounceApiUrlResolver.GetConfiguredDomain(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", prevDd);
            Environment.SetEnvironmentVariable("XRAY_DOMAIN", prevSingle);
        }
    }

    [Fact]
    public void GetConfiguredDomain_FallsBackToXrayDomainEnv()
    {
        var prevDd = Environment.GetEnvironmentVariable("XRAY__DOMAIN");
        var prevSingle = Environment.GetEnvironmentVariable("XRAY_DOMAIN");
        try
        {
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", null);
            Environment.SetEnvironmentVariable("XRAY_DOMAIN", "from-single.example.com");
            var config = new ConfigurationBuilder().Build();

            Assert.Equal("from-single.example.com", VpnServerAnnounceApiUrlResolver.GetConfiguredDomain(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XRAY__DOMAIN", prevDd);
            Environment.SetEnvironmentVariable("XRAY_DOMAIN", prevSingle);
        }
    }
}
