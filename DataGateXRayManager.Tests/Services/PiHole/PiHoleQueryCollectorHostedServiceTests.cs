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
    public async Task CollectOnceAsync_BroadcastsBatch_WithNullCommonName()
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

        var sut = new PiHoleQueryCollectorHostedService(
            store,
            api.Object,
            cursor.Object,
            new PiHoleCollectorStatusStore(),
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(1, count);
        cursor.Verify(x => x.SaveLastUntilUtc(It.IsAny<DateTimeOffset>()), Times.Once);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(sentArgs);
        var batch = Assert.IsType<DnsQueryBatchRequest>(sentArgs![0]);
        Assert.Single(batch.Queries);
        Assert.Null(batch.Queries[0].CommonName);
        Assert.Equal("10.80.0.3", batch.Queries[0].ClientIp);
        Assert.Equal("openai.com", batch.Queries[0].Domain);
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
