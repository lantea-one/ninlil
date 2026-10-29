// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class lists, in order, the schema steps this build of Ninlil knows for MongoDB. A
///     schema step is a data reshaping that MongoDB, having no migrations, needs at startup (a
///     backfill), or a change of document shape that an older build would misread. The
///     <see cref="MongoStartupGuard" /> records each applied step in the
///     <c>NinlilDnsSchemaSteps</c> collection and refuses to start when that collection holds a
///     step this list does not contain, which means a newer Ninlil version has used the database.
///     Add a step to <see cref="Known" /> whenever a backfill is added or a document shape changes
///     in a way an older build would misread; never rename or remove one.
/// </summary>
public static class MongoSchemaSteps
{
    /// <summary>
    ///     This constant names the step that moves a legacy known-client identifier into its own
    ///     address document (see <see cref="DnsClientAddressLegacyBackfill" />).
    /// </summary>
    public const string DnsClientAddressLegacyBackfill = nameof(DnsClientAddressLegacyBackfill);

    /// <summary>
    ///     This constant names the step that links each cluster peer key to its peer (see
    ///     <see cref="ClusterKeyLinkLegacyBackfill" />).
    /// </summary>
    public const string ClusterKeyLinkLegacyBackfill = nameof(ClusterKeyLinkLegacyBackfill);

    /// <summary>
    ///     This constant names the step that marks zone change journal entries carrying the SOA
    ///     before and after each change. It has no backfill: entries written earlier keep no SOA
    ///     history. Recording it makes an older build refuse a database this version has used, as
    ///     the relational engines do after the matching migration.
    /// </summary>
    public const string ZoneJournalSoaHistory = nameof(ZoneJournalSoaHistory);

    /// <summary>
    ///     This property contains every step this build knows, in the order they run.
    /// </summary>
    public static IReadOnlyList<string> Known { get; } =
    [DnsClientAddressLegacyBackfill, ClusterKeyLinkLegacyBackfill, ZoneJournalSoaHistory];
}
