using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleSubnetFilterTests
{
    [Theory]
    [InlineData("10.80.0.2", "10.80.0", false, true)]
    [InlineData("10.80.01.2", "10.80.0", false, false)]
    [InlineData("10.80.0.2", "10.80.0.", false, true)]
    [InlineData("10.80.1.2", "10.80.0.", false, false)]
    [InlineData("10.80.0.2", null, false, true)]
    [InlineData("10.80.0.2", "", false, true)]
    [InlineData("10.80.0.2", null, true, false)]
    [InlineData("10.80.0.2", "", true, false)]
    [InlineData("10.80.0.2", "10.80.0.", true, true)]
    public void Matches_RespectsPrefix(string clientIp, string? prefix, bool requirePrefix, bool expected) =>
        Assert.Equal(expected, PiHoleSubnetFilter.Matches(clientIp, prefix, requirePrefix));

    [Theory]
    [InlineData("10.80.0", "10.80.0.")]
    [InlineData("10.80.0.", "10.80.0.")]
    [InlineData(" 10.51.15 ", "10.51.15.")]
    public void NormalizePrefix_AppendsTrailingDot(string raw, string expected) =>
        Assert.Equal(expected, PiHoleSubnetFilter.NormalizePrefix(raw));

    [Fact]
    public void Apply_FiltersRecordsBySubnet()
    {
        var records = new[]
        {
            new PiHoleQueryRecord(1, "10.80.0.1", "a.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "10.80.1.1", "b.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(3, "172.20.0.2", "c.example", null, "FORWARDED", DateTimeOffset.UtcNow),
        };

        var filtered = PiHoleSubnetFilter.Apply(records, "10.80.0.");

        Assert.Single(filtered);
        Assert.Equal("a.example", filtered[0].Domain);
    }

    [Fact]
    public void Apply_WhenPrefixEmptyAndRequired_ReturnsNone()
    {
        var records = new[]
        {
            new PiHoleQueryRecord(1, "10.80.0.1", "a.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "172.20.0.2", "b.example", null, "FORWARDED", DateTimeOffset.UtcNow),
        };

        var filtered = PiHoleSubnetFilter.Apply(records, null, requirePrefix: true);
        Assert.Empty(filtered);
    }

    [Fact]
    public void ApplyExcludes_DropsOpenVpnLanPrefixes()
    {
        var records = new[]
        {
            new PiHoleQueryRecord(1, "10.51.15.7", "ovpn.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "10.80.0.2", "xray.example", null, "FORWARDED", DateTimeOffset.UtcNow),
        };

        var filtered = PiHoleSubnetFilter.ApplyExcludes(records, "10.51.15.,10.51.16.");
        Assert.Single(filtered);
        Assert.Equal("10.80.0.2", filtered[0].ClientIp);
    }
}
