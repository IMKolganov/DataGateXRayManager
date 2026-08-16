using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Dto;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Requests;
using DataGateXRayManager.Hubs;
using DataGateXRayManager.Models;
using DataGateXRayManager.Services.PiHole;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleQueryCollectorHostedServiceTests
{
    [Fact]
    public async Task CollectOnceAsync_ForwardsOnlyEnrichedWithCommonName()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult
            {
                TotalFromApi = 2,
                Records =
                [
                    new PiHoleQueryRecord(10, "10.80.0.3", "openai.com", "A", "FORWARDED", DateTimeOffset.UtcNow),
                    new PiHoleQueryRecord(11, "10.51.15.4", "stolen.com", "A", "FORWARDED", DateTimeOffset.UtcNow)
                ]
            });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var identity = new Mock<IPiHoleClientIdentityResolver>();
        identity.Setup(x => x.EnrichAsync(It.IsAny<IEnumerable<PiHoleQueryRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DnsQueryEventDto>
            {
                new()
                {
                    PiHoleQueryId = 10,
                    ClientIp = "10.80.0.3",
                    CommonName = "user-cn",
                    Domain = "openai.com",
                    QueryType = "A",
                    Status = "FORWARDED",
                    QueriedAtUtc = DateTimeOffset.UtcNow
                },
                new()
                {
                    PiHoleQueryId = 11,
                    ClientIp = "10.51.15.4",
                    CommonName = null,
                    Domain = "stolen.com",
                    QueryType = "A",
                    Status = "FORWARDED",
                    QueriedAtUtc = DateTimeOffset.UtcNow
                }
            });

        object?[]? sentArgs = null;
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync(
                "DnsQueriesReceived",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => sentArgs = args)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<XRayEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var store = PiHoleRuntimeOptionsStoreTestHelper.Create();
        store.Apply(new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60, Enabled = true });

        var status = new PiHoleCollectorStatusStore();
        var sut = new PiHoleQueryCollectorHostedService(
            store,
            api.Object,
            cursor.Object,
            status,
            identity.Object,
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(1, count);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(sentArgs);
        var batch = Assert.IsType<DnsQueryBatchRequest>(sentArgs![0]);
        Assert.Single(batch.Queries);
        Assert.Equal("user-cn", batch.Queries[0].CommonName);
        Assert.Equal(1, status.GetSnapshot().LastPollQueriesEnriched);
        Assert.Equal(1, status.GetSnapshot().LastPollQueriesForwarded);
    }

    [Fact]
    public async Task CollectOnceAsync_DoesNotForward_WhenNoCommonName()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult
            {
                TotalFromApi = 1,
                Records =
                [
                    new PiHoleQueryRecord(10, "10.80.0.3", "openai.com", "A", "FORWARDED", DateTimeOffset.UtcNow)
                ]
            });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var identity = new Mock<IPiHoleClientIdentityResolver>();
        identity.Setup(x => x.EnrichAsync(It.IsAny<IEnumerable<PiHoleQueryRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DnsQueryEventDto>
            {
                new()
                {
                    PiHoleQueryId = 10,
                    ClientIp = "10.80.0.3",
                    CommonName = null,
                    Domain = "openai.com",
                    QueriedAtUtc = DateTimeOffset.UtcNow
                }
            });

        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<XRayEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var status = new PiHoleCollectorStatusStore();
        var sut = new PiHoleQueryCollectorHostedService(
            PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions { Enabled = true }),
            api.Object,
            cursor.Object,
            status,
            identity.Object,
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(0, count);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(0, status.GetSnapshot().LastPollQueriesForwarded);
    }

    [Fact]
    public async Task CollectOnceAsync_AdvancesCursor_WhenNoRecords()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult { TotalFromApi = 3, Records = [] });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var identity = new Mock<IPiHoleClientIdentityResolver>();
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<XRayEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var status = new PiHoleCollectorStatusStore();
        var sut = new PiHoleQueryCollectorHostedService(
            PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions { Enabled = true }),
            api.Object,
            cursor.Object,
            status,
            identity.Object,
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(0, count);
        cursor.Verify(x => x.SaveLastUntilUtc(It.IsAny<DateTimeOffset>()), Times.Once);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var snapshot = status.GetSnapshot();
        Assert.Equal(3, snapshot.LastPollQueriesFetched);
        Assert.Equal(0, snapshot.LastPollQueriesForwarded);
    }
}
