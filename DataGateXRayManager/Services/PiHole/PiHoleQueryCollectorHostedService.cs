using DataGateXRayManager.Hubs;
using DataGateXRayManager.Models;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Requests;
using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Services.PiHole;

/// <summary>
/// Polls Pi-hole on the Xray host and forwards DNS batches to the dashboard via SignalR
/// (<c>DnsQueriesReceived</c> on <see cref="XRayEventHub"/>), same pattern as OpenVPN.
/// CommonName comes from store IdentityIp matching (requires XRAY_DNS_IDENTITY_* + client DNS via tunnel).
/// </summary>
public sealed class PiHoleQueryCollectorHostedService(
    IPiHoleRuntimeOptionsStore runtimeOptions,
    IPiHoleApiClient piHoleApiClient,
    IPiHoleQueryCursorStore cursorStore,
    IPiHoleCollectorStatusStore statusStore,
    IPiHoleClientIdentityResolver identityResolver,
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
                if (string.IsNullOrWhiteSpace(cfg.ClientSubnetPrefix))
                {
                    logger.LogWarning(
                        "Pi-hole ClientSubnetPrefix is empty — Xray will not ingest DNS rows (shared Pi-hole would include OpenVPN clients). Set prefix to identity pool (e.g. 10.80.0.) when using XRAY_DNS_IDENTITY_*.");
                }

                logger.LogInformation(
                    "Pi-hole DNS collector started. BaseUrl={BaseUrl}, IntervalSec={IntervalSec}, BatchSize={BatchSize}, LookbackSec={LookbackSec}, SubnetPrefix={SubnetPrefix}, ExcludePrefixes={ExcludePrefixes}",
                    cfg.BaseUrl,
                    cfg.PollIntervalSeconds,
                    cfg.BatchSize,
                    cfg.LookbackSeconds,
                    cfg.ClientSubnetPrefix,
                    cfg.ClientSubnetExcludePrefixes);
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
        // When the API window was truncated by BatchSize, only advance to the newest fetched row
        // so the remainder is retried on the next poll (lookback dedupes overlaps).
        var cursorTarget = untilUtc;
        if (fetch.MayHaveMore)
        {
            cursorTarget = fetch.NewestFetchedAtUtc
                           ?? (records.Count > 0 ? records.Max(r => r.QueriedAtUtc) : untilUtc);
            logger.LogInformation(
                "Pi-hole poll hit BatchSize={BatchSize} (apiTotal={ApiTotal}); advancing cursor only to {Cursor:o} to avoid skipping queries.",
                cfg.BatchSize,
                fetch.TotalFromApi,
                cursorTarget);
        }

        if (records.Count == 0)
        {
            cursorStore.SaveLastUntilUtc(cursorTarget);
            statusStore.RecordPollSuccess(new PiHolePollSuccessResult
            {
                AtUtc = pollStarted,
                QueriesFetched = fetch.TotalFromApi,
                QueriesAfterFilter = 0,
                QueriesEnriched = 0,
                QueriesForwarded = 0,
                CursorUntilUtc = cursorTarget
            });
            return 0;
        }

        var enriched = await identityResolver.EnrichAsync(records, cancellationToken);
        var mapped = enriched.Where(q => !string.IsNullOrWhiteSpace(q.CommonName)).ToList();

        if (mapped.Count == 0)
        {
            cursorStore.SaveLastUntilUtc(cursorTarget);
            statusStore.RecordPollSuccess(new PiHolePollSuccessResult
            {
                AtUtc = pollStarted,
                QueriesFetched = fetch.TotalFromApi,
                QueriesAfterFilter = records.Count,
                QueriesEnriched = 0,
                QueriesForwarded = 0,
                CursorUntilUtc = cursorTarget
            });
            logger.LogInformation(
                "Pi-hole poll OK: apiTotal={ApiTotal}, afterFilter={AfterFilter}, enriched=0, forwarded=0 (no IdentityIp match — enable XRAY_DNS_IDENTITY_* and client DNS via tunnel)",
                fetch.TotalFromApi,
                records.Count);
            return 0;
        }

        var batch = new DnsQueryBatchRequest
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Queries = mapped
        };

        // Cursor advances only after SignalR accepts the send — otherwise retry the same window.
        await eventHub.Clients.All.SendAsync("DnsQueriesReceived", batch, cancellationToken);
        cursorStore.SaveLastUntilUtc(cursorTarget);
        statusStore.RecordPollSuccess(new PiHolePollSuccessResult
        {
            AtUtc = pollStarted,
            QueriesFetched = fetch.TotalFromApi,
            QueriesAfterFilter = records.Count,
            QueriesEnriched = mapped.Count,
            QueriesForwarded = mapped.Count,
            CursorUntilUtc = cursorTarget
        });

        logger.LogInformation(
            "Pi-hole poll OK: apiTotal={ApiTotal}, afterFilter={AfterFilter}, enriched={Enriched}, forwarded={Forwarded}",
            fetch.TotalFromApi,
            records.Count,
            mapped.Count,
            mapped.Count);
        return mapped.Count;
    }
}
