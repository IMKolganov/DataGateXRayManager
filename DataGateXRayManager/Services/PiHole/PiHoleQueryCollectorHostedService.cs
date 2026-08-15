using DataGateXRayManager.Hubs;
using DataGateXRayManager.Models;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Dto;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Requests;
using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Services.PiHole;

/// <summary>
/// Polls Pi-hole on the Xray host and forwards DNS batches to the dashboard via SignalR
/// (<c>DnsQueriesReceived</c> on <see cref="XRayEventHub"/>), same pattern as OpenVPN.
/// CommonName is left null until Xray exposes a Pi-hole-visible per-client IP.
/// </summary>
public sealed class PiHoleQueryCollectorHostedService(
    IPiHoleRuntimeOptionsStore runtimeOptions,
    IPiHoleApiClient piHoleApiClient,
    IPiHoleQueryCursorStore cursorStore,
    IPiHoleCollectorStatusStore statusStore,
    IHubContext<XRayEventHub> eventHub,
    ILogger<PiHoleQueryCollectorHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private bool _collectorActive;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cfg = runtimeOptions.GetEffective();
            if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.BaseUrl))
            {
                if (_collectorActive)
                {
                    _collectorActive = false;
                    statusStore.SetCollectorRunning(false);
                    logger.LogInformation(
                        "Pi-hole DNS collector paused. Enabled={Enabled}, HasBaseUrl={HasBaseUrl}.",
                        cfg.Enabled,
                        !string.IsNullOrWhiteSpace(cfg.BaseUrl));
                }

                try
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            if (!_collectorActive)
            {
                _collectorActive = true;
                statusStore.SetCollectorRunning(true);
                logger.LogInformation(
                    "Pi-hole DNS collector started. BaseUrl={BaseUrl}, IntervalSec={IntervalSec}, BatchSize={BatchSize}, LookbackSec={LookbackSec}, SubnetPrefix={SubnetPrefix}",
                    cfg.BaseUrl,
                    cfg.PollIntervalSeconds,
                    cfg.BatchSize,
                    cfg.LookbackSeconds,
                    cfg.ClientSubnetPrefix);
            }

            var interval = TimeSpan.FromSeconds(Math.Max(10, cfg.PollIntervalSeconds));

            try
            {
                await CollectOnceAsync(cfg, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                statusStore.RecordPollFailure(DateTimeOffset.UtcNow, ex.Message);
                logger.LogWarning(
                    ex,
                    "Pi-hole poll cycle failed. BaseUrl={BaseUrl}, LastError={Error}",
                    cfg.BaseUrl,
                    ex.Message);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _collectorActive = false;
        statusStore.SetCollectorRunning(false);
        logger.LogInformation("Pi-hole DNS collector stopped.");
    }

    internal async Task<int> CollectOnceAsync(PiHoleOptions cfg, CancellationToken cancellationToken)
    {
        var pollStarted = DateTimeOffset.UtcNow;
        var untilUtc = pollStarted;
        var lastUntil = cursorStore.GetLastUntilUtc();
        var fromUtc = lastUntil?.AddSeconds(-Math.Max(0, cfg.LookbackSeconds))
                      ?? untilUtc.AddSeconds(-Math.Max(cfg.LookbackSeconds, cfg.PollIntervalSeconds));

        if (fromUtc >= untilUtc)
            fromUtc = untilUtc.AddSeconds(-5);

        var fetch = await piHoleApiClient.GetQueriesSinceAsync(
            fromUtc,
            untilUtc,
            cfg.BatchSize,
            cancellationToken);

        var records = fetch.Records;
        if (records.Count == 0)
        {
            cursorStore.SaveLastUntilUtc(untilUtc);
            statusStore.RecordPollSuccess(new PiHolePollSuccessResult
            {
                AtUtc = pollStarted,
                QueriesFetched = fetch.TotalFromApi,
                QueriesAfterFilter = 0,
                QueriesEnriched = 0,
                QueriesForwarded = 0,
                CursorUntilUtc = untilUtc
            });
            return 0;
        }

        // No tun VirtualAddress on Xray — forward IP/domain rows with CommonName null.
        var queries = records.Select(r => new DnsQueryEventDto
        {
            PiHoleQueryId = r.PiHoleQueryId,
            ClientIp = r.ClientIp,
            CommonName = null,
            Domain = r.Domain,
            QueryType = r.QueryType,
            Status = r.Status,
            QueriedAtUtc = r.QueriedAtUtc
        }).ToList();

        var batch = new DnsQueryBatchRequest
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Queries = queries
        };

        await eventHub.Clients.All.SendAsync("DnsQueriesReceived", batch, cancellationToken);
        cursorStore.SaveLastUntilUtc(untilUtc);
        statusStore.RecordPollSuccess(new PiHolePollSuccessResult
        {
            AtUtc = pollStarted,
            QueriesFetched = fetch.TotalFromApi,
            QueriesAfterFilter = records.Count,
            QueriesEnriched = 0,
            QueriesForwarded = queries.Count,
            CursorUntilUtc = untilUtc
        });

        logger.LogInformation(
            "Pi-hole poll OK: apiTotal={ApiTotal}, afterFilter={AfterFilter}, forwarded={Forwarded} (CN mapping N/A for Xray)",
            fetch.TotalFromApi,
            records.Count,
            queries.Count);
        return queries.Count;
    }
}
