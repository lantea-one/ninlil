using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Configuration;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This class is the receiving side of cluster sync for settings sections, the
///     <see cref="IClusterConfigSyncApplier" /> that applies what
///     <see cref="ClusterConfigSyncSettingsRecorder" /> sends. When the incoming write is newer
///     than the local one, it merges the values into the runtime configuration file and records
///     the new last-write-wins state. It lives here rather than in
///     <c>Ninlil.Dns.Server.ClusterSync</c> with the entity-row appliers built on
///     <c>ClusterConfigSyncApplierBase&lt;TEntity&gt;</c> because it needs
///     <see cref="RuntimeConfigurationOptions" />, which only <c>Ninlil.Web</c> declares, so it is
///     registered in <c>Program.cs</c> rather than by <c>AddNinlilDnsServerClusterSync</c>.
/// </summary>
/// <param name="stateRepository">Reads and writes the sync state of a section.</param>
/// <param name="unitOfWork">Commits the state write.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file an incoming write is merged into.</param>
/// <param name="dataProtectionProvider">
///     Supplies this node's key ring, which encrypts a secret in an incoming write before it
///     reaches the file (see <see cref="ConfigurationSecretSettings.ProtectForFile" />).
/// </param>
/// <param name="logger">The logger a malformed envelope is recorded to.</param>
public class SettingsClusterConfigSyncApplier(
    IClusterSyncedSettingsStateRepository stateRepository,
    IUnitOfWork unitOfWork,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider,
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
        // A settings envelope's EntityKey is the section name (for example "DnsServer:Cookies"),
        // not a GUID as for entity rows, so the local state row is looked up by name.
        string sectionName = envelope.EntityKey;

        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            sectionName,
            stoppingToken
        );

        // A section with no local state row has never been written here, so it counts as old as
        // possible and any incoming write wins, as for a missing entity row in
        // ClusterConfigSyncApplierBase<TEntity>.
        DateTimeOffset localLastModifiedUtc =
            state?.GetLastModifiedUtc() ?? DateTimeOffset.MinValue;

        // Only a newer write (or the winner of the deterministic tie-break) overwrites the file.
        // A stale or duplicate envelope, delivered out of order or twice, is dropped, as in
        // ClusterConfigSyncApplierBase<TEntity>.ApplyAsync.
        bool incomingWins = ClusterSyncConflictResolver.IncomingWins(
            localLastModifiedUtc,
            state?.OriginatingNodeId,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId
        );

        if (!incomingWins)
            return;

        // PayloadJson comes from a peer. A malformed body (a different schema, a peer on older
        // code) is logged and dropped rather than thrown, which would stop the config-sync
        // subscriber from processing the envelopes after it.
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

        // Turn each SettingsSyncEntry back into the PresetConfigurationSetting that
        // PresetConfigurationFileApplier takes (see SettingsSyncEntry for why the wire shape
        // differs).
        List<PresetConfigurationSetting> settings =
        [
            .. entries.Select(entry => new PresetConfigurationSetting(
                entry.SectionPath,
                entry.Key,
                entry.Value
            )),
        ];

        // A secret travels in the clear on the cluster channel (TLS and TSIG protected, see
        // ClusterConfigSyncSettingsRecorder) because a Data Protection payload only opens with
        // the key ring that made it. It must not reach this node's file in the clear: a copy of
        // the file or of a backup would expose it. Encrypting it here, with this node's own key
        // ring, keeps each node's file readable only by that node, and an older peer that
        // already sent plaintext is protected on arrival the same way.
        settings =
        [
            .. ConfigurationSecretSettings.ProtectForFile(settings, dataProtectionProvider),
        ];

        // Merge the values into the runtime configuration file through the same atomic
        // read-merge-write path the settings controllers use, so a synced write and a local one
        // get the same file safety.
        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        // Record the new last-write-wins state with the envelope's origin node, not this node, so
        // a later write from a third node is compared against the node that wrote the value now
        // on disk.
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
