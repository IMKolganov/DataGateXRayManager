using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleSubnetFilterTests
{
    [Theory]
    [InlineData("10.80.0.2", "10.80.0.", true)]
    [InlineData("10.80.1.2", "10.80.0.", false)]
    [InlineData("10.80.0.2", null, true)]
    [InlineData("10.80.0.2", "", true)]
    [InlineData("10.80.0.2", "   ", true)]
    // StartsWith hazard: prefix "10.8" also matches "10.80..."
    [InlineData("10.80.0.2", "10.8", true)]
    [InlineData("10.9.0.2", "10.8", false)]
    public void Matches_RespectsPrefix(string clientIp, string? prefix, bool expected) =>
        Assert.Equal(expected, PiHoleSubnetFilter.Matches(clientIp, prefix));

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
    public void Apply_WhenPrefixEmpty_ReturnsAll()
    {
        var records = new[]
        {
            new PiHoleQueryRecord(1, "10.80.0.1", "a.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "172.20.0.2", "b.example", null, "FORWARDED", DateTimeOffset.UtcNow),
        };

        var filtered = PiHoleSubnetFilter.Apply(records, null);
        Assert.Equal(2, filtered.Count);
    }
}
