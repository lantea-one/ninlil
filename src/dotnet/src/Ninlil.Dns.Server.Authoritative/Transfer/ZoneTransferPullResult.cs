using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class represents one difference sequence within an RFC 1995 IXFR pull's response: the
///     records removed and added to move the zone from <see cref="OldSerial" /> to
///     <see cref="NewSerial" />, mirroring exactly what <see cref="ZoneChangeJournalEntryEntity" />
///     already records on the serving side.
/// </summary>
/// <param name="OldSerial">The serial this step starts from.</param>
/// <param name="Removed">The records this step removes.</param>
/// <param name="NewSerial">The serial this step ends at.</param>
/// <param name="Added">The records this step adds.</param>
public sealed record ZoneTransferDiffStep(
    uint OldSerial,
    IReadOnlyList<DnsResourceRecord> Removed,
    uint NewSerial,
    IReadOnlyList<DnsResourceRecord> Added
);

/// <summary>
///     This class carries the outcome of asking <see cref="IZoneTransferClient" /> to pull a
///     transfer: either it failed, for a reason a caller can log, or it succeeded, as either a
///     full record set (AXFR, or an IXFR request the primary chose to answer as one) or an
///     ordered sequence of incremental steps (a real IXFR diff).
/// </summary>
public sealed class ZoneTransferPullResult
{
    /// <summary>
    ///     This property indicates whether the pull succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    ///     This property contains why the pull failed, or <see langword="null" /> when
    ///     <see cref="Succeeded" /> is <see langword="true" />.
    /// </summary>
    public string FailureReason { get; }

    /// <summary>
    ///     This property indicates whether the response was an incremental diff
    ///     (<see cref="DiffSteps" />) rather than a full record set (<see cref="FullRecordSet" />).
    /// </summary>
    public bool IsIncremental { get; }

    /// <summary>
    ///     This property indicates whether the primary reported that this server was already
    ///     current -- in which case both <see cref="FullRecordSet" /> and
    ///     <see cref="DiffSteps" /> are empty, and there is nothing to apply.
    /// </summary>
    public bool IsAlreadyCurrent { get; }

    /// <summary>
    ///     This property contains the serial the zone will be at once this result is applied.
    /// </summary>
    public uint FinalSerial { get; }

    /// <summary>
    ///     This property contains every record the zone should end up with, including its own
    ///     SOA record, when <see cref="IsIncremental" /> is <see langword="false" />.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> FullRecordSet { get; }

    /// <summary>
    ///     This property contains the ordered incremental steps to apply, when
    ///     <see cref="IsIncremental" /> is <see langword="true" />.
    /// </summary>
    public IReadOnlyList<ZoneTransferDiffStep> DiffSteps { get; }

    /// <summary>
    ///     This constructor is private; construct an instance through <see cref="Failure" />,
    ///     <see cref="AlreadyCurrent" />, <see cref="Full" />, or <see cref="Incremental" /> instead.
    /// </summary>
    private ZoneTransferPullResult(
        bool succeeded,
        string failureReason,
        bool isIncremental,
        bool isAlreadyCurrent,
        uint finalSerial,
        IReadOnlyList<DnsResourceRecord> fullRecordSet,
        IReadOnlyList<ZoneTransferDiffStep> diffSteps
    )
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        IsIncremental = isIncremental;
        IsAlreadyCurrent = isAlreadyCurrent;
        FinalSerial = finalSerial;
        FullRecordSet = fullRecordSet;
        DiffSteps = diffSteps;
    }

    /// <summary>
    ///     This method builds the result for a pull that failed outright.
    /// </summary>
    public static ZoneTransferPullResult Failure(string reason) =>
        new(false, reason, false, false, 0, [], []);

    /// <summary>
    ///     This method builds the result for a pull that found nothing new to apply.
    /// </summary>
    public static ZoneTransferPullResult AlreadyCurrent(uint serial) =>
        new(true, null, false, true, serial, [], []);

    /// <summary>
    ///     This method builds the result for a full record set (AXFR, or an IXFR the primary
    ///     chose to answer that way).
    /// </summary>
    public static ZoneTransferPullResult Full(
        uint finalSerial,
        IReadOnlyList<DnsResourceRecord> records
    ) => new(true, null, false, false, finalSerial, records, []);

    /// <summary>
    ///     This method builds the result for a real incremental diff.
    /// </summary>
    public static ZoneTransferPullResult Incremental(
        uint finalSerial,
        IReadOnlyList<ZoneTransferDiffStep> steps
    ) => new(true, null, true, false, finalSerial, [], steps);
}
