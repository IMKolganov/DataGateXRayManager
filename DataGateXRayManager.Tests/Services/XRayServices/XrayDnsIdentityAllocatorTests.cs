using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayDnsIdentityAllocatorTests
{
    [Fact]
    public void AllocateNext_SkipsNetworkGatewayAndUsed()
    {
        var first = XrayDnsIdentityAllocator.AllocateNext("10.80.0.0/24", []);
        Assert.Equal("10.80.0.2", first);

        var second = XrayDnsIdentityAllocator.AllocateNext("10.80.0.0/24", ["10.80.0.2"]);
        Assert.Equal("10.80.0.3", second);
    }

    [Fact]
    public void EnsureIdentityIps_BackfillsAndClearsRevoked()
    {
        var store = new List<StoredXRayClient>
        {
            new() { CommonName = "a", Uuid = "1", IsRevoked = false },
            new() { CommonName = "b", Uuid = "2", IsRevoked = true, IdentityIp = "10.80.0.9" }
        };

        Assert.True(XrayDnsIdentityAllocator.EnsureIdentityIps(store, "10.80.0.0/24"));
        Assert.Equal("10.80.0.2", store[0].IdentityIp);
        Assert.Null(store[1].IdentityIp);
    }

    [Fact]
    public void EnsureIdentityIps_RecyclesAfterRevoke()
    {
        var store = new List<StoredXRayClient>
        {
            new() { CommonName = "a", Uuid = "1", IsRevoked = true, IdentityIp = "10.80.0.2" },
            new() { CommonName = "b", Uuid = "2", IsRevoked = false }
        };

        Assert.True(XrayDnsIdentityAllocator.EnsureIdentityIps(store, "10.80.0.0/24"));
        Assert.Null(store[0].IdentityIp);
        Assert.Equal("10.80.0.2", store[1].IdentityIp);
    }

    [Fact]
    public void FindCommonNameByIdentityIp_MatchesActiveOnly()
    {
        var store = new List<StoredXRayClient>
        {
            new() { CommonName = "user-a", Uuid = "1", IdentityIp = "10.80.0.5", IsRevoked = false },
            new() { CommonName = "user-b", Uuid = "2", IdentityIp = "10.80.0.6", IsRevoked = true }
        };

        Assert.Equal("user-a", XrayDnsIdentityAllocator.FindCommonNameByIdentityIp(store, "10.80.0.5"));
        Assert.Null(XrayDnsIdentityAllocator.FindCommonNameByIdentityIp(store, "10.80.0.6"));
        Assert.Null(XrayDnsIdentityAllocator.FindCommonNameByIdentityIp(store, "10.80.0.99"));
    }

    [Fact]
    public void AllocateNext_InvalidCidr_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            XrayDnsIdentityAllocator.AllocateNext("not-a-cidr", []));
    }

    [Fact]
    public void AllocateNext_PoolExhausted_ReturnsNull()
    {
        Assert.Null(XrayDnsIdentityAllocator.AllocateNext("10.80.0.0/32", []));
        Assert.Null(XrayDnsIdentityAllocator.AllocateNext("10.80.0.0/31", []));
    }

    [Fact]
    public void EnsureIdentityIps_PoolExhausted_Throws()
    {
        var store = new List<StoredXRayClient>
        {
            new() { CommonName = "a", Uuid = "1", IsRevoked = false }
        };
        Assert.Throws<InvalidOperationException>(() =>
            XrayDnsIdentityAllocator.EnsureIdentityIps(store, "10.80.0.0/31"));
    }

    [Fact]
    public void FindCommonNameByIdentityIp_DuplicateIdentityIp_ReturnsNull()
    {
        var store = new List<StoredXRayClient>
        {
            new() { CommonName = "user-a", Uuid = "1", IdentityIp = "10.80.0.5", IsRevoked = false },
            new() { CommonName = "user-b", Uuid = "2", IdentityIp = "10.80.0.5", IsRevoked = false }
        };

        Assert.Null(XrayDnsIdentityAllocator.FindCommonNameByIdentityIp(store, "10.80.0.5"));
    }

    [Fact]
    public void SuggestedClientSubnetPrefix_FromCidr()
    {
        Assert.Equal("10.80.0.", XrayDnsIdentityAllocator.SuggestedClientSubnetPrefix("10.80.0.0/24"));
        Assert.Equal("10.80.1.", XrayDnsIdentityAllocator.SuggestedClientSubnetPrefix("10.80.1.0/24"));
    }

    [Fact]
    public void ClientSubnetPrefixCoversIdentityPool_DetectsMismatch()
    {
        Assert.True(XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool("10.80.0.", "10.80.0.0/24"));
        Assert.True(XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool("10.80.0", "10.80.0.0/24"));
        Assert.False(XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool("10.80.1.", "10.80.0.0/24"));
        Assert.False(XrayDnsIdentityAllocator.ClientSubnetPrefixCoversIdentityPool("", "10.80.0.0/24"));
    }
}
