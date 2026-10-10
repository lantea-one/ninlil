// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This enum names the database a scope reads from. <see cref="StorageReadIntent" /> holds the
///     value a scope asks for and <see cref="IStorageReadRouter" /> decides whether the request is
///     honoured.
/// </summary>
public enum StorageReadTargetEnum
{
    /// <summary>
    ///     The read-write database. Every write, migration and snapshot, and every read that must
    ///     see the latest committed data, uses it. This is the default.
    /// </summary>
    Primary,

    /// <summary>
    ///     A read-only replica, when one is configured and the router lets the scope use it. The
    ///     scope may see data that is a little behind the primary, and it must not write.
    /// </summary>
    Replica,
}
