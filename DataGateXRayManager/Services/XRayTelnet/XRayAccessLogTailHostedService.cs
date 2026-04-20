using DataGateXRayManager.Helpers;
using DataGateXRayManager.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DataGateXRayManager.Services.XRayTelnet;

/// <summary>
/// Tails XRay access.log (if configured): raw lines to /hubs/xray subscribers; JSON lines also to /hubs/xray-event as
/// <see cref="DataGateMonitor.SharedModels.DataGateXRayManager.VpnEvent.AccessLogEventDto"/> when parseable.
/// </summary>
public class XRayAccessLogTailHostedService(
    IConfiguration configuration,
    IDataPathResolver dataPathResolver,
    XRayManagementSignalService management,
    IHubContext<XRayEventHub> eventHub,
    ILogger<XRayAccessLogTailHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = configuration["XRay:AccessLogPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogInformation("XRay:AccessLogPath not set; access log tail disabled.");
            return;
        }

        var path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(dataPathResolver.GetDataPath(), configured);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            await using (File.Create(path)) { }
        }

        logger.LogInformation("Tailing XRay access log at {Path}", path);

        long position = new FileInfo(path).Length;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, stoppingToken);
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < position)
                    position = 0;
                fs.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                while (true)
                {
                    var line = await reader.ReadLineAsync(stoppingToken);
                    if (line is null)
                        break;
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    var trimmed = line.Trim();
                    management.BroadcastLine(trimmed, stoppingToken);

                    if (XRayAccessLogLineParser.TryParseStructured(trimmed, out var structured) && structured is not null)
                    {
                        try
                        {
                            await eventHub.Clients.All.SendAsync("AccessLogRecord", structured, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "AccessLogRecord broadcast failed");
                        }
                    }
                    else
                    {
                        try
                        {
                            await eventHub.Clients.All.SendAsync("AccessLogRaw", trimmed, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "AccessLogRaw broadcast failed");
                        }
                    }
                }

                position = fs.Length;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Access log tail error");
            }
        }
    }
}
