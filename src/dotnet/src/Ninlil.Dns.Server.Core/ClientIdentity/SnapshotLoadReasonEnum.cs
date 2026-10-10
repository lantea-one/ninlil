// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This enum tells the loader of a <see cref="RefreshingSnapshot{T}" /> why it runs, so the
///     loader can choose which database to read. Only <see cref="TimeToLive" /> may read a
///     replica.
/// </summary>
public enum SnapshotLoadReasonEnum
{
    /// <summary>
    ///     The snapshot has no value yet. The load runs on the primary so the first copy is
    ///     current.
    /// </summary>
    Initial,

    /// <summary>
    ///     The value only got older than its time-to-live. A replica that is a few seconds behind
    ///     is good enough, because nothing known has changed.
    /// </summary>
    TimeToLive,

    /// <summary>
    ///     A local write expired the value, or a caller asked for an explicit refresh. The load
    ///     runs on the primary: a replica may not have the write yet.
    /// </summary>
    Expired,
}
