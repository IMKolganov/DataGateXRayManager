using System.Net;
using DataGateXRayManager.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateXRayManager.Tests.Services;

public class ExternalIpAddressServiceTests
{
    private readonly Mock<ILogger<ExternalIpAddressService>> _logger = new();

    private static IConfiguration BuildConfig(params string[] urls)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < urls.Length; i++)
            dict[$"ExternalIpServices:{i}"] = urls[i];
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_WhenNoServices_ReturnsNull()
    {
        var sut = new ExternalIpAddressService(
            _logger.Object,
            BuildConfig(),
            new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))),
            new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await sut.GetPublicIpAddressAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_PrefersConfiguredPublicIp_WithoutHttp()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("203.0.113.1")
            });
        });
        var dict = new Dictionary<string, string?>
        {
            ["PUBLIC_IP"] = "81.27.109.193",
            ["ExternalIpServices:0"] = "https://example.test/ip"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var sut = new ExternalIpAddressService(
            _logger.Object,
            config,
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("81.27.109.193", await sut.GetPublicIpAddressAsync(CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_ReturnsTrimmedIp_AndCaches()
    {
        const string url = "https://example.test/ip";
        var calls = 0;
        var handler = new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(" 203.0.113.88 \n")
            });
        });
        var sut = new ExternalIpAddressService(
            _logger.Object, BuildConfig(url), new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("203.0.113.88", await sut.GetPublicIpAddressAsync(CancellationToken.None));
        Assert.Equal("203.0.113.88", await sut.GetPublicIpAddressAsync(CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_WhenFirstFails_UsesSecond()
    {
        const string url1 = "https://a.test/ip";
        const string url2 = "https://b.test/ip";
        var handler = new FakeHandler((req, _) =>
        {
            if (req.RequestUri!.ToString() == url1)
                throw new HttpRequestException("down");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("198.51.100.9")
            });
        });
        var sut = new ExternalIpAddressService(
            _logger.Object, BuildConfig(url1, url2), new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("198.51.100.9", await sut.GetPublicIpAddressAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_WhenAllFail_ReturnsNull_AndCachesNegative()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new HttpRequestException("down");
        });
        var sut = new ExternalIpAddressService(
            _logger.Object,
            BuildConfig("https://a.test/ip"),
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await sut.GetPublicIpAddressAsync(CancellationToken.None));
        Assert.Null(await sut.GetPublicIpAddressAsync(CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_SkipsHtmlAndUsesNextProvider()
    {
        const string url1 = "https://a.test/ip";
        const string url2 = "https://b.test/ip";
        var handler = new FakeHandler((req, _) =>
        {
            var body = req.RequestUri!.ToString() == url1
                ? "<html>rate limited</html>"
                : "203.0.113.44";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        });
        var sut = new ExternalIpAddressService(
            _logger.Object, BuildConfig(url1, url2), new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("203.0.113.44", await sut.GetPublicIpAddressAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetPublicIpAddressAsync_SkipsIpv6_AndUsesNextProvider()
    {
        const string url1 = "https://a.test/ip";
        const string url2 = "https://b.test/ip";
        var handler = new FakeHandler((req, _) =>
        {
            var body = req.RequestUri!.ToString() == url1 ? "2001:db8::1" : "198.51.100.20";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        });
        var sut = new ExternalIpAddressService(
            _logger.Object, BuildConfig(url1, url2), new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("198.51.100.20", await sut.GetPublicIpAddressAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    public void TryParsePublicIp_RejectsInvalid(string? raw)
    {
        Assert.False(ExternalIpAddressService.TryParsePublicIp(raw, out _));
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("  198.51.100.1 \n", "198.51.100.1")]
    public void TryParsePublicIp_AcceptsPublicIpv4(string raw, string expected)
    {
        Assert.True(ExternalIpAddressService.TryParsePublicIp(raw, out var ip));
        Assert.Equal(expected, ip);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
