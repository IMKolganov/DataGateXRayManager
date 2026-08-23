using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayClientDnsInfoTests
{
    [Fact]
    public void BuildClientDnsServers_TrimsDedupesAndSkipsEmpty()
    {
        var list = XrayClientDnsInfo.BuildClientDnsServers(" 172.20.0.1 ", "172.20.0.1");
        Assert.Equal(["172.20.0.1"], list);
    }

    [Fact]
    public void BuildClientDnsServers_IncludesBothWhenDistinct()
    {
        var list = XrayClientDnsInfo.BuildClientDnsServers("172.20.0.1", "1.1.1.1");
        Assert.Equal(["172.20.0.1", "1.1.1.1"], list);
    }

    [Fact]
    public void BuildClientDnsServers_BothEmpty_ReturnsEmptyList()
    {
        Assert.Empty(XrayClientDnsInfo.BuildClientDnsServers(null, "  "));
        Assert.Empty(XrayClientDnsInfo.BuildClientDnsServers(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build()));
    }

    [Fact]
    public void BuildClientDnsServers_FromConfiguration_ReadsDns1Dns2()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DNS1"] = "172.20.0.1",
            ["DNS2"] = "8.8.4.4",
        }).Build();
        Assert.Equal(["172.20.0.1", "8.8.4.4"], XrayClientDnsInfo.BuildClientDnsServers(config));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDnsIdentityEnabled_ReadsEnv(string? raw, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XRAY_DNS_IDENTITY_ENABLED"] = raw
        }).Build();
        Assert.Equal(expected, XrayClientDnsInfo.IsDnsIdentityEnabled(config));
    }

    [Fact]
    public void IsDnsIdentityEnabled_ReadsNestedConfigKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Xray:DnsIdentity:Enabled"] = "true"
        }).Build();
        Assert.True(XrayClientDnsInfo.IsDnsIdentityEnabled(config));
    }

    [Fact]
    public void ToDnsServersJson_IsRawJsonArray()
    {
        Assert.Equal("""["172.20.0.1"]""", XrayClientDnsInfo.ToDnsServersJson(["172.20.0.1"]));
        Assert.Equal("[]", XrayClientDnsInfo.ToDnsServersJson([]));
    }
}
