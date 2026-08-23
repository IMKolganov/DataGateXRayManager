namespace DataGateXRayManager.Services.PiHole;

public sealed class PiHoleQueryFetchResult
{
    public IReadOnlyList<PiHoleQueryRecord> Records { get; init; } = Array.Empty<PiHoleQueryRecord>();

    public int TotalFromApi { get; init; }

    /// <summary>
    /// True when the API page was cut off by <c>maxCount</c> — caller must not advance the cursor to poll-until
    /// or later queries in the window are permanently skipped.
    /// </summary>
    public bool MayHaveMore { get; init; }

    /// <summary>Newest <see cref="PiHoleQueryRecord.QueriedAtUtc"/> among raw API rows (before subnet filter).</summary>
    public DateTimeOffset? NewestFetchedAtUtc { get; init; }
}

public interface IPiHoleApiClient
{
    Task<PiHoleQueryFetchResult> GetQueriesSinceAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc,
        int maxCount,
        CancellationToken cancellationToken);

    Task<(bool Authenticated, int SampleQueryCount, string? Error)> ProbeAsync(CancellationToken cancellationToken);
}
