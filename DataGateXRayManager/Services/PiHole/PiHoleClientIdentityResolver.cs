using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Dto;
using DataGateXRayManager.Helpers;
using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Services.PiHole;

public interface IPiHoleClientIdentityResolver
{
    Task<IReadOnlyList<DnsQueryEventDto>> EnrichAsync(
        IEnumerable<PiHoleQueryRecord> records,
        CancellationToken cancellationToken);
}

/// <summary>
/// Maps Pi-hole ClientIp → CommonName via store IdentityIp (OpenVPN VirtualAddress analogue).
/// </summary>
public sealed class PiHoleClientIdentityResolver(
    IDataPathResolver dataPathResolver,
    IXrayClientStore clientStore) : IPiHoleClientIdentityResolver
{
    public async Task<IReadOnlyList<DnsQueryEventDto>> EnrichAsync(
        IEnumerable<PiHoleQueryRecord> records,
        CancellationToken cancellationToken)
    {
        var dataDir = Path.GetFullPath(dataPathResolver.GetDataPath());
        var store = await clientStore.LoadAsync(dataDir, cancellationToken);

        return records
            .Select(record =>
            {
                var cn = XrayDnsIdentityAllocator.FindCommonNameByIdentityIp(store, record.ClientIp);
                return new DnsQueryEventDto
                {
                    PiHoleQueryId = record.PiHoleQueryId,
                    ClientIp = record.ClientIp,
                    CommonName = cn,
                    Domain = record.Domain,
                    QueryType = record.QueryType,
                    Status = record.Status,
                    QueriedAtUtc = record.QueriedAtUtc
                };
            })
            .ToList();
    }
}
