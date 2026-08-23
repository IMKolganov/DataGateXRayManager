using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleBaseUrlGuardTests
{
    [Theory]
    [InlineData("http://172.17.0.1:8080")]
    [InlineData("https://pihole.example.com/")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://host.docker.internal:8080")]
    public void EnsureSafeOrThrow_AllowsNormalUrls(string url) =>
        PiHoleBaseUrlGuard.EnsureSafeOrThrow(url);

    [Theory]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("ftp://172.17.0.1/")]
    [InlineData("not-a-url")]
    public void EnsureSafeOrThrow_RejectsUnsafeUrls(string url) =>
        Assert.Throws<InvalidOperationException>(() => PiHoleBaseUrlGuard.EnsureSafeOrThrow(url));
}
