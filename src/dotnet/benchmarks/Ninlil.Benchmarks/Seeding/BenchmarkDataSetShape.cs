// Define our namespace.
namespace Ninlil.Benchmarks.Seeding;

/// <summary>
///     This record holds the row counts of one data set, so the generator and the benchmarks
///     agree on what exists. <see cref="BenchmarkDataSet.ShapeFor" /> returns the shape of a
///     <see cref="BenchmarkScaleEnum" />.
/// </summary>
/// <param name="QueryLogEntries">Query-log rows, spread over seven days.</param>
/// <param name="QueryLogClientAddresses">Distinct client addresses in the query log.</param>
/// <param name="QueryLogClientIdentifiers">Distinct client identifiers in the query log.</param>
/// <param name="QueryLogNames">Distinct queried names in the query log.</param>
/// <param name="Zones">Ordinary zones, each with <paramref name="RecordsPerZone" /> records.</param>
/// <param name="RecordsPerZone">Records in each ordinary zone.</param>
/// <param name="BigZoneRecords">Records in the one large zone.</param>
/// <param name="PtrRecords">Records managed by the PTR feature, in the first ordinary zone.</param>
/// <param name="FilterLists">Subscribed filter lists.</param>
/// <param name="EntriesPerList">Entries in each subscribed filter list.</param>
/// <param name="LocalListEntries">Entries in the one local filter list.</param>
/// <param name="ClientGroups">Client groups, not counting the default group.</param>
/// <param name="Clients">Clients.</param>
/// <param name="ClientAddresses">Client addresses, spread over the clients.</param>
/// <param name="ClientSchedules">Client schedules.</param>
/// <param name="Rewrites">DNS rewrites.</param>
/// <param name="ForwarderZones">Forwarder zones.</param>
/// <param name="GlobalForwarders">Global forwarders.</param>
/// <param name="DhcpScopes">DHCPv4 scopes.</param>
/// <param name="Leases">Leases, spread over the scopes.</param>
/// <param name="Reservations">DHCP reservations, spread over the scopes.</param>
/// <param name="WebhookAttempts">Webhook delivery attempts of the one subscription.</param>
/// <param name="Users">Users, each with three roles.</param>
public sealed record BenchmarkDataSetShape(
    int QueryLogEntries,
    int QueryLogClientAddresses,
    int QueryLogClientIdentifiers,
    int QueryLogNames,
    int Zones,
    int RecordsPerZone,
    int BigZoneRecords,
    int PtrRecords,
    int FilterLists,
    int EntriesPerList,
    int LocalListEntries,
    int ClientGroups,
    int Clients,
    int ClientAddresses,
    int ClientSchedules,
    int Rewrites,
    int ForwarderZones,
    int GlobalForwarders,
    int DhcpScopes,
    int Leases,
    int Reservations,
    int WebhookAttempts,
    int Users
);
