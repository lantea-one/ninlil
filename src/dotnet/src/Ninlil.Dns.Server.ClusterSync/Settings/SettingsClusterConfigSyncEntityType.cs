// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Settings;

/// <summary>
///     This class holds the entity type of settings-section envelopes, shared by
///     <see cref="SettingsClusterSyncEventSubscriber" /> (sending) and
///     <c>SettingsClusterConfigSyncApplier</c> (receiving). The applier lives in
///     <c>Ninlil.Web</c> because it needs <c>RuntimeConfigurationOptions</c>, which is defined
///     there.
/// </summary>
public static class SettingsClusterConfigSyncEntityType
{
    /// <summary>
    ///     This constant contains the entity type of every settings-section envelope. For this
    ///     type <see cref="Ninlil.Dns.Server.Core.ClusterSync.ClusterConfigSyncEnvelope.EntityKey" />
    ///     is the section's name (for example <c>"EventBroker"</c>), not a <see cref="Guid" />.
    /// </summary>
    public const string EntityTypeName = "Settings";
}
