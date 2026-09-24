// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record is returned once a restore has finished successfully -- every entity has
///     already been wiped and re-imported, and the Data Protection key ring, TLS certificate, and
///     runtime configuration file have already been overwritten on disk by the time this is
///     produced. A restore never takes effect for a still-running process's own already-open
///     database connection, already-loaded key ring, or already-bound TLS certificate, so the
///     caller must be told plainly that a restart is required.
/// </summary>
/// <param name="TotalEntitiesImported">
///     The total number of rows imported across every entity type, not counting identity
///     relationships.
/// </param>
/// <param name="EntityCounts">How many rows of each entity type were imported, by name.</param>
public sealed record BackupRestoreSummary(
    int TotalEntitiesImported,
    IReadOnlyDictionary<string, int> EntityCounts
);
