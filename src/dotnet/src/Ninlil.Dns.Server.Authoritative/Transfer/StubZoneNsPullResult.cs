using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class carries the outcome of asking <see cref="StubZoneNsPuller" /> to learn a
///     delegation's NS records: either it failed, for a reason a caller can log, or it succeeded
///     with the NS records (and any accompanying address glue) the primary returned. Deliberately
///     separate from <see cref="ZoneTransferPullResult" /> -- a <see cref="ZoneTypeEnum.Stub" />
///     zone has no serial of its own to track and never distinguishes a full transfer from an
///     incremental one, so that type's own richer shape (already-current/full/incremental) would
///     not actually fit this simpler exchange.
/// </summary>
public sealed class StubZoneNsPullResult
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
    ///     This property contains the NS records the primary returned, plus any A/AAAA glue it
    ///     also provided for those name servers -- empty when <see cref="Succeeded" /> is
    ///     <see langword="false" />.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> Records { get; }

    /// <summary>
    ///     This constructor is private; construct an instance through <see cref="Failure" /> or
    ///     <see cref="Success" /> instead.
    /// </summary>
    private StubZoneNsPullResult(
        bool succeeded,
        string failureReason,
        IReadOnlyList<DnsResourceRecord> records
    )
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        Records = records;
    }

    /// <summary>
    ///     This method builds the result for a pull that failed outright.
    /// </summary>
    public static StubZoneNsPullResult Failure(string reason) => new(false, reason, []);

    /// <summary>
    ///     This method builds the result for a pull that succeeded.
    /// </summary>
    public static StubZoneNsPullResult Success(IReadOnlyList<DnsResourceRecord> records) =>
        new(true, null, records);
}
