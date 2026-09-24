// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Settings;

/// <summary>
///     This class holds the one shared entity-type discriminator every settings-section
///     config-sync envelope carries -- shared between this project's own sending-side
///     <see cref="SettingsClusterSyncEventSubscriber" /> and <c>Ninlil.Web</c>'s own
///     receiving-side <c>SettingsClusterConfigSyncApplier</c> (which lives in
///     <c>Ninlil.Web</c>, not here, since only that project holds
///     <c>RuntimeConfigurationOptions</c> -- see that applier's own doc comment).
/// </summary>
public static class SettingsClusterConfigSyncEntityType
{
    /// <summary>
    ///     This constant is the entity-type discriminator every settings-section envelope this
    ///     project ever sends or receives carries. <see cref="Ninlil.Dns.Server.Core.ClusterSync.ClusterConfigSyncEnvelope.EntityKey" />
    ///     for this type is always the section's own stable name (for example
    ///     <c>"EventBroker"</c>), never a <see cref="Guid" />.
    /// </summary>
    public const string EntityTypeName = "Settings";
}
