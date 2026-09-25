using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class carries the result of <see cref="StubZoneNsPuller" />: a failure with a reason
///     to log, or the NS records and any A/AAAA glue the primary returned. It is separate from
///     <see cref="ZoneTransferPullResult" /> because a <see cref="ZoneTypeEnum.Stub" /> zone
///     tracks no serial and has no full or incremental transfer.
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
    ///     This property contains the NS records the primary returned plus any A/AAAA glue, or
    ///     an empty list on failure.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> Records { get; }

    /// <summary>
    ///     This constructor is private; use <see cref="Failure" /> or <see cref="Success" />.
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
