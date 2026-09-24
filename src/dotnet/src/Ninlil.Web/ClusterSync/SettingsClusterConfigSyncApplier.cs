using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a settings
///     section -- deliberately <b>not</b> placed in <c>Ninlil.Dns.Server.ClusterSync</c> alongside
///     the entity-row appliers built on <c>ClusterConfigSyncApplierBase&lt;TEntity&gt;</c>, since
///     applying a settings write needs <see cref="RuntimeConfigurationOptions" />, which only this
///     project (<c>Ninlil.Web</c>) declares. Registered directly in <c>Program.cs</c>, not
///     through that project's own <c>AddNinlilDnsServerClusterSync</c> extension.
/// </summary>
/// <param name="stateRepository">The repository this class reads and writes a section's own sync state through.</param>
/// <param name="unitOfWork">The unit of work the state write is committed through.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file an incoming write is merged into.</param>
/// <param name="logger">The logger a malformed envelope is recorded to.</param>
public class SettingsClusterConfigSyncApplier(
    IClusterSyncedSettingsStateRepository stateRepository,
    IUnitOfWork unitOfWork,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ILogger<SettingsClusterConfigSyncApplier> logger
) : IClusterConfigSyncApplier
{
    /// <inheritdoc />
    public string EntityType => SettingsClusterConfigSyncEntityType.EntityTypeName;

    /// <inheritdoc />
    public async Task ApplyAsync(
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        // A settings envelope's own EntityKey carries the section's stable name (for example
        // "EventBroker"), not a GUID -- unlike the entity-row appliers built on
        // ClusterConfigSyncApplierBase<TEntity>, so this section's own local sync-state row is
        // looked up by that name directly.
        string sectionName = envelope.EntityKey;

        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            sectionName,
            stoppingToken
        );

        // A section never synced to this node before has no local state row at all -- treat it as
        // "as old as possible" so any real incoming write always wins the comparison below, the
        // same first-write bootstrap ClusterConfigSyncApplierBase<TEntity>'s own local-is-null
        // case handles for an entity row.
        DateTimeOffset localLastModifiedUtc =
            state?.GetLastModifiedUtc() ?? DateTimeOffset.MinValue;

        // Only overwrite this node's own runtime configuration file if the incoming change is
        // genuinely newer (or wins the deterministic tie-break) -- a stale or duplicate envelope
        // (arrived out of order, or re-delivered) is silently discarded, mirroring
        // ClusterConfigSyncApplierBase<TEntity>.ApplyAsync's own already-tracked-entity branch.
        bool incomingWins = ClusterSyncConflictResolver.IncomingWins(
            localLastModifiedUtc,
            state?.OriginatingNodeId,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId
        );

        if (!incomingWins)
            return;

        // The envelope's own PayloadJson is peer-supplied data, not something this node produced
        // itself -- a malformed body (a mismatched schema version, a peer running older code) is
        // logged and dropped rather than allowed to throw out of this applier and break the
        // config-sync subscriber's own processing of every other pending envelope.
        List<SettingsSyncEntry> entries;
        try
        {
            entries =
                JsonSerializer.Deserialize<List<SettingsSyncEntry>>(envelope.PayloadJson) ?? [];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Received a malformed settings config-sync envelope for section '{SectionName}' "
                    + "-- ignored.",
                sectionName
            );
            return;
        }

        // Convert each wire-safe SettingsSyncEntry back into the PresetConfigurationSetting shape
        // PresetConfigurationFileApplier actually understands (see SettingsSyncEntry's own doc
        // comment for why the wire shape itself is not that type directly).
        List<PresetConfigurationSetting> settings =
        [
            .. entries.Select(entry => new PresetConfigurationSetting(
                entry.SectionPath,
                entry.Key,
                entry.Value
            )),
        ];

        // Merge the incoming values into this node's own runtime configuration file -- the same
        // atomic read-then-merge-then-write path every settings controller already uses locally,
        // so a cluster-propagated write and an operator's own direct write go through identical
        // file-safety guarantees.
        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        // Record this section's own new last-write-wins bookkeeping, stamped with the envelope's
        // own originating node id (never this node's own id) so a later write from a third node
        // compares against who actually authored the value currently on disk.
        if (state is null)
        {
            state = new ClusterSyncedSettingsStateEntity
            {
                Id = Guid.NewGuid(),
                SectionName = sectionName,
                CreatedAtUtc = envelope.LastModifiedUtc,
                OriginatingNodeId = envelope.OriginatingNodeId,
            };
            await stateRepository.AddAsync(state, stoppingToken);
        }
        else
        {
            state.UpdatedAtUtc = envelope.LastModifiedUtc;
            state.OriginatingNodeId = envelope.OriginatingNodeId;
            stateRepository.Update(state);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
