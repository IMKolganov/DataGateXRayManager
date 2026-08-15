using System.Net;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Enums;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using DataGateXRayManager.Services.Proxy;

namespace DataGateXRayManager.Tests.Services.Proxy;

public class XRayProxyRealIpEnricherTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.1:53188", true)]
    [InlineData("172.20.0.2", true)]
    [InlineData("172.20.0.2:443", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("192.168.1.10:1194", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("203.0.113.5:443", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NeedsProxyEnrichment_ClassifiesEndpoints(string? remote, bool expected)
    {
        Assert.Equal(expected, XRayProxyRealIpEnricher.NeedsProxyEnrichment(remote));
    }

    [Theory]
    [InlineData("203.0.113.9", 443, "203.0.113.9:443")]
    [InlineData("203.0.113.9", 0, "203.0.113.9")]
    [InlineData("10.0.0.1", 443, null)]
    [InlineData(null, 443, null)]
    [InlineData("  ", 443, null)]
    public void FormatProxyRealIp_FormatsAndRejectsPrivate(string? ip, int port, string? expected)
    {
        Assert.Equal(expected, XRayProxyRealIpEnricher.FormatProxyRealIp(ip, port));
    }

    [Fact]
    public void Enrich_ByPortAcrossDifferentHosts_DoesNotMatch()
    {
        // LocalProxyIp must equal Xray peer; cross-host ephemeral port collision is not enough.
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "any", RemoteAddress = "172.20.0.2:41810" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 41810, "198.51.100.10", 5555, commonName: null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_ByCommonNameAlone_DoesNotMatch_WhenHostsDiffer()
    {
        // Unauthenticated clientRef must not attribute another user's IP.
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "victim-cn", RemoteAddress = "172.20.0.2" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 40000, "198.51.100.10", 5555, "victim-cn")
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_ByCommonName_DisambiguatesDuplicateHostPorts()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "user-a", RemoteAddress = "172.20.0.2:41810" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "172.20.0.2", 41810, "198.51.100.1", 1, "user-b"),
            Hint("c2", "172.20.0.2", 41810, "198.51.100.2", 2, "user-a"),
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Equal("198.51.100.2:2", clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_ByLocalProxyHostAndPort_WhenSameHost()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "unknown-cn", RemoteAddress = "127.0.0.1:41810" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 41810, "198.51.100.7", 8443, commonName: null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Equal("198.51.100.7:8443", clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_ByUniqueLocalProxyHost_WhenNoPort()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "a", RemoteAddress = "172.20.0.2" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "172.20.0.2", 1111, "203.0.113.50", 1, null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Equal("203.0.113.50:1", clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_OneToOneFallback_Removed()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "a", RemoteAddress = "172.20.0.2" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 5000, "203.0.113.77", 443, null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_AmbiguousWithoutPort_DoesNotGuess()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "a", RemoteAddress = "172.20.0.2" },
            new() { Email = "b", RemoteAddress = "172.20.0.3" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 1, "203.0.113.1", 1, null),
            Hint("c2", "127.0.0.1", 2, "203.0.113.2", 2, null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
        Assert.Null(clients[1].ProxyRealIp);
    }

    [Fact]
    public void Enrich_PublicRemoteAddress_LeftUntouched()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "a", RemoteAddress = "203.0.113.9:443" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "127.0.0.1", 443, "198.51.100.1", 9, "a")
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_RejectsPrivateRealClientIp()
    {
        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "a", RemoteAddress = "172.20.0.2:9" }
        };
        var hints = new List<ProxySessionHint>
        {
            Hint("c1", "172.20.0.2", 9, "10.0.0.5", 1, null)
        };

        XRayProxyRealIpEnricher.Enrich(clients, hints);

        Assert.Null(clients[0].ProxyRealIp);
    }

    [Fact]
    public void Enrich_ViaActiveProxyConnectionService_HostPortMatch()
    {
        var svc = new ActiveProxyConnectionService();
        svc.Add(new ActiveProxyConnection
        {
            ConnectionId = "x1",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "198.51.100.20",
            RealClientPort = 1234,
            LocalProxyIp = "172.20.0.2",
            LocalProxyPort = 9,
            ConnectedAtUtc = DateTime.UtcNow
        }, commonName: "cn-1");

        var clients = new List<XrayClientSessionDto>
        {
            new() { Email = "cn-1", RemoteAddress = "172.20.0.2:9" }
        };

        XRayProxyRealIpEnricher.Enrich(clients, svc);

        Assert.Equal("198.51.100.20:1234", clients[0].ProxyRealIp);
        Assert.Equal("cn-1", svc.GetCommonName("x1"));
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("203.0.113.1", false)]
    public void IsPrivateOrLoopback_Ipv4Ranges(string ip, bool expected)
    {
        Assert.Equal(expected, XRayProxyRealIpEnricher.IsPrivateOrLoopback(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::1", false)]
    public void IsPrivateOrLoopback_Ipv6Ranges(string ip, bool expected)
    {
        Assert.Equal(expected, XRayProxyRealIpEnricher.IsPrivateOrLoopback(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("fc00::1", true)]
    [InlineData("[fe80::1]:443", true)]
    [InlineData("2001:db8::1", false)]
    public void NeedsProxyEnrichment_Ipv6(string? remote, bool expected)
    {
        Assert.Equal(expected, XRayProxyRealIpEnricher.NeedsProxyEnrichment(remote));
    }

    private static ProxySessionHint Hint(
        string id,
        string localIp,
        int localPort,
        string realIp,
        int realPort,
        string? commonName) =>
        new(
            new ActiveProxyConnection
            {
                ConnectionId = id,
                Protocol = ProxyConnectionProtocol.Tcp,
                LocalProxyIp = localIp,
                LocalProxyPort = localPort,
                RealClientIp = realIp,
                RealClientPort = realPort,
                ConnectedAtUtc = DateTime.UtcNow
            },
            commonName);
}
