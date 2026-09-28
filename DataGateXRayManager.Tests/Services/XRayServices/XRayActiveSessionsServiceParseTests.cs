using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy;
using DataGateMonitor.SharedModels.DataGateXRayManager.Proxy.Enums;
using DataGateMonitor.SharedModels.DataGateXRayManager.XrayClients;
using DataGateXRayManager.Services.Proxy;
using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XRayActiveSessionsServiceParseTests
{
    [Fact]
    public void ParseGetUsersStats_ReadsTrafficAndPrefersPrivateIpForSessionKey()
    {
        const string json = """
            {
              "users": [
                {
                  "email": "cn-1",
                  "traffic": { "uplink": 11, "downlink": 22 },
                  "ips": [
                    { "ip": "203.0.113.9", "lastSeen": 1719043300 },
                    { "ip": "172.20.0.2", "lastSeen": 1719043200 }
                  ]
                }
              ]
            }
            """;

        var list = XRayActiveSessionsService.ParseGetUsersStats(json);

        Assert.Single(list);
        Assert.Equal("cn-1", list[0].Session.Email);
        Assert.Equal("172.20.0.2", list[0].Session.RemoteAddress);
        Assert.Equal(11, list[0].Session.BytesReceived);
        Assert.Equal(22, list[0].Session.BytesSent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1719043200), list[0].Session.ConnectedSince);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1719043300), list[0].LastSeenUtc);
    }

    [Fact]
    public void ParseGetUsersStats_WhenOnlyPrivateIp_UsesIt()
    {
        const string json = """
            {
              "users": [
                {
                  "email": "cn-docker",
                  "ips": [ { "ip": "172.20.0.2", "lastSeen": 1719043200 } ]
                }
              ]
            }
            """;

        var list = XRayActiveSessionsService.ParseGetUsersStats(json);
        Assert.Equal("172.20.0.2", list[0].Session.RemoteAddress);
    }

    [Fact]
    public void ParseGetUsersStats_EmptyIps_StillReturnsUserWithoutLastSeen()
    {
        const string json = """
            { "users": [ { "email": "lonely", "traffic": { "uplink": 1, "downlink": 2 }, "ips": [] } ] }
            """;

        var list = XRayActiveSessionsService.ParseGetUsersStats(json);
        Assert.Single(list);
        Assert.Equal("", list[0].Session.RemoteAddress);
        Assert.Equal(1, list[0].Session.BytesReceived);
        Assert.Null(list[0].LastSeenUtc);
    }

    [Fact]
    public void ParseSingleUserOnlineIpList_MapShape_PrefersPrivate()
    {
        const string json = """
            {
              "ips": {
                "198.51.100.7": 1719043400,
                "172.20.0.2": 1719043200
              }
            }
            """;

        var observation = XRayActiveSessionsService.ParseSingleUserOnlineIpList(json, "user-a");
        Assert.NotNull(observation);
        Assert.Equal("172.20.0.2", observation!.Session.RemoteAddress);
        Assert.Equal("user-a", observation.Session.Email);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1719043200), observation.Session.ConnectedSince);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1719043400), observation.LastSeenUtc);
    }

    [Fact]
    public void ParseSingleUserOnlineIpList_NullIps_ReturnsEmptyRemote()
    {
        var observation = XRayActiveSessionsService.ParseSingleUserOnlineIpList("""{"ips":null}""", "e");
        Assert.NotNull(observation);
        Assert.Equal("", observation!.Session.RemoteAddress);
        Assert.Null(observation.LastSeenUtc);
    }

    [Fact]
    public void EnrichAfterParse_FillsProxyRealIp_ForPrivatePeerWithPort()
    {
        var clients = XRayActiveSessionsService.ParseGetUsersStats("""
            {
              "users": [
                {
                  "email": "adg-76-cn",
                  "ips": [ { "ip": "172.20.0.2:41810", "lastSeen": 1719043200 } ]
                }
              ]
            }
            """).Select(o => o.Session).ToList();

        var proxies = new ActiveProxyConnectionService();
        proxies.Add(new ActiveProxyConnection
        {
            ConnectionId = "p1",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "203.0.113.44",
            RealClientPort = 443,
            LocalProxyIp = "172.20.0.2",
            LocalProxyPort = 41810,
            ConnectedAtUtc = DateTime.UtcNow
        }, commonName: "adg-76-cn");

        XRayProxyRealIpEnricher.Enrich(clients, proxies);

        Assert.Equal("172.20.0.2:41810", clients[0].RemoteAddress);
        Assert.Equal("203.0.113.44:443", clients[0].ProxyRealIp);
    }
}
