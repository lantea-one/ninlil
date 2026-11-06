using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Web.Configuration;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for settings sections, the reading
///     half of <see cref="SettingsClusterConfigSyncApplier" />. A backfill to a node that joined
///     later calls it to send each synced section whole, because a live envelope carries only the
///     keys one write changed. It is stage <see cref="ClusterConfigBackfillStages.Independent" />
///     and is registered in <c>Program.cs</c> beside the applier, because it needs
///     <see cref="RuntimeConfigurationOptions" />, which only <c>Ninlil.Web</c> declares.
///     <para>
///         For each section of <see cref="ClusterSyncedSettingsCatalog" /> that has a
///         <see cref="ClusterSyncedSettingsStateEntity" /> row on this node, it sends every
///         catalogued key the runtime configuration file holds, stamped with the row's time and
///         origin. A section with no row has never been written or synced here and sends
///         nothing. The envelope has the shape the recorder publishes
///         (<see cref="SettingsSyncEntry" /> list), so the peer's
///         <see cref="SettingsClusterConfigSyncApplier" /> applies it like a live one.
///     </para>
///     <para>
///         <b>Values come from the file, never from <c>IConfiguration</c>.</b> An environment
///         variable overrides a file value on the node that has it (the README's cluster
///         section says it pins a synced value on its node). Reading the bound options would send that
///         pinned value to peers that have no such variable, so the file, which holds only what
///         an operator saved or a peer synced, is the source
///         (<see cref="RuntimeConfigurationFileReader" />). A key the file lacks is not sent, and
///         the peer keeps its own.
///     </para>
///     <para>
///         <b>Secrets travel as plaintext, never as ciphertext.</b> A secret in the file is stored
///         encrypted with this node's key ring (<see cref="ConfigurationSecretSettings" />) and
///         opens nowhere else. The source decrypts it with this node's key ring and sends the
///         plaintext under the plain key, exactly as the recorder sends a secret as the operator
///         entered it. That is safe only inside the pinned-TLS, signed cluster channel, and the
///         receiver encrypts it again with its own key ring. A payload this key ring cannot open
///         is left out with a warning, so the peer keeps its own secret instead of receiving
///         nothing it can use.
///     </para>
/// </summary>
/// <param name="stateRepository">Reads the last-write-wins state row of each section.</param>
/// <param name="runtimeConfigurationOptions">Names the runtime configuration file.</param>
/// <param name="dataProtectionProvider">Supplies this node's key ring, which opens an encrypted secret.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger an unreadable file or secret is reported to.</param>
public sealed class SettingsClusterConfigSyncSource(
    IClusterSyncedSettingsStateRepository stateRepository,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<SettingsClusterConfigSyncSource> logger
) : IClusterConfigSyncSource
{
    /// <inheritdoc />
    public string EntityType => SettingsClusterConfigSyncEntityType.EntityTypeName;

    /// <inheritdoc />
    public int BackfillStage => ClusterConfigBackfillStages.Independent;

    /// <inheritdoc />
    public async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        JsonObject root;
        try
        {
            root = await RuntimeConfigurationFileReader.ReadRootAsync(
                runtimeConfigurationOptions.ConfigurationFilePath,
                stoppingToken
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(
                exception,
                "The runtime configuration file could not be read, so no settings are sent to the peer."
            );
            yield break;
        }

        if (root is null)
            yield break;

        Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

        foreach (ClusterSyncedSettingsSection section in ClusterSyncedSettingsCatalog.Sections)
        {
            stoppingToken.ThrowIfCancellationRequested();

            ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
                section.SectionName,
                stoppingToken
            );

            if (state is null)
                continue;

            List<SettingsSyncEntry> entries = [];

            foreach ((string sectionPath, string key) in section.Keys)
            {
                if (
                    TryReadValue(root, section.SectionName, sectionPath, key, out JsonElement value)
                )
                    entries.Add(new SettingsSyncEntry(sectionPath, key, value));
            }

            // An envelope with no key would still move the peer's last-write-wins state forward
            // and so block an older, real write, while changing nothing.
            if (entries.Count == 0)
                continue;

            // The key is the section name, not the row id, so the stamping rule is borrowed and
            // the key replaced.
            yield return ClusterConfigSyncEnvelopes.ForEntity(
                EntityType,
                state,
                SettingsSyncEntry.ToPayloadJson(entries),
                ownNodeId
            ) with
            {
                EntityKey = section.SectionName,
            };
        }
    }

    /// <summary>
    ///     This method reads one catalogued key from the file. A secret's plaintext key yields the
    ///     plaintext, taken from the file when it holds one and otherwise decrypted from the
    ///     encrypted sibling.
    /// </summary>
    /// <returns>
    ///     <see langword="false" /> when the file does not hold the key, or holds a secret this
    ///     node's key ring cannot open.
    /// </returns>
    private bool TryReadValue(
        JsonObject root,
        string sectionName,
        string sectionPath,
        string key,
        out JsonElement value
    )
    {
        value = default;

        if (RuntimeConfigurationFileReader.FindSection(root, sectionPath) is not JsonObject section)
            return false;

        ConfigurationSecretSetting secret = ConfigurationSecretSettings.FindByPlainKey(
            sectionPath,
            key
        );

        if (secret is null)
        {
            if (!section.TryGetPropertyValue(key, out JsonNode node))
                return false;

            value = JsonSerializer.SerializeToElement(node);
            return true;
        }

        string plaintext = ReadText(section, secret.PlainKey, out bool plainPresent);

        if (string.IsNullOrEmpty(plaintext))
        {
            string payload = ReadText(section, secret.EncryptedKey, out bool encryptedPresent);

            if (!string.IsNullOrEmpty(payload))
            {
                try
                {
                    plaintext = ConfigurationSecretSettings.Unprotect(
                        secret,
                        dataProtectionProvider,
                        payload
                    );
                }
                catch (CryptographicException exception)
                {
                    logger.LogWarning(
                        exception,
                        "The secret {Setting} in section '{SectionName}' was not protected with this "
                            + "node's key ring and is not sent to the peer, which keeps its own.",
                        secret.DisplayName,
                        sectionName
                    );
                    return false;
                }
            }
            else if (!plainPresent && !encryptedPresent)
            {
                return false;
            }

            // Both keys present and empty is how the file records a cleared secret, and a clear is
            // part of the section's state, so it is sent as an empty value.
            plaintext ??= string.Empty;
        }

        value = JsonSerializer.SerializeToElement(plaintext);
        return true;
    }

    /// <summary>
    ///     This method reads a string value from a section object, or <see langword="null" /> for
    ///     a missing key, a JSON null or a value that is not a string.
    /// </summary>
    private static string ReadText(JsonObject section, string key, out bool present)
    {
        present = section.TryGetPropertyValue(key, out JsonNode node);

        return node is JsonValue jsonValue && jsonValue.TryGetValue(out string text) ? text : null;
    }
}
