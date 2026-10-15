// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class is the single classification point for every <see cref="IDnsServerEvent" />
///     type. Each type has a short, stable name, used as
///     <see cref="DnsServerEventEnvelope.EventTypeName" /> and as webhook subscription event
///     name, and <see cref="DnsServerEventDeliveryFlags" /> that decide whether the type may be
///     dropped under load and whether it crosses the message broker. Explicit names, rather than
///     <see cref="Type.FullName" />, survive a class rename or namespace move without breaking
///     queued broker messages or stored webhook subscriptions.
///     <para>
///         Every event type is listed here. Local delivery never depends on registration: the
///         publisher delivers an unlisted type to local subscribers and only skips the broker for
///         it. A test fails when an event class is added without an entry.
///     </para>
///     <para>
///         <see cref="ClusterConfigSettingsChangedEvent" /> has no
///         <see cref="DnsServerEventDeliveryFlags.CrossesBroker" /> because it carries settings
///         values in clear, secrets included; it never leaves the process.
///     </para>
/// </summary>
public static class DnsServerEventTypeRegistry
{
    private const DnsServerEventDeliveryFlags Broker = DnsServerEventDeliveryFlags.CrossesBroker;

    private const DnsServerEventDeliveryFlags LossyBroker =
        DnsServerEventDeliveryFlags.Lossy | DnsServerEventDeliveryFlags.CrossesBroker;

    private static readonly Dictionary<
        string,
        (Type Type, DnsServerEventDeliveryFlags Flags)
    > Entries = new()
    {
        ["DnsQueryReceived"] = (typeof(DnsQueryReceivedEvent), LossyBroker),
        ["DnsQueryAnswered"] = (typeof(DnsQueryAnsweredEvent), LossyBroker),
        ["DnsZoneChanged"] = (typeof(DnsZoneChangedEvent), Broker),
        ["DnsCacheEvicted"] = (typeof(DnsCacheEvictedEvent), Broker),
        // Events chosen as webhook subscription targets, alongside the ones above.
        ["DnssecZoneResigningCompleted"] = (typeof(DnssecZoneResigningCompletedEvent), Broker),
        ["AcmeCertificateRenewalSucceeded"] = (
            typeof(AcmeCertificateRenewalSucceededEvent),
            Broker
        ),
        ["AcmeCertificateRenewalFailed"] = (typeof(AcmeCertificateRenewalFailedEvent), Broker),
        ["CloudDnsZoneSyncFailed"] = (typeof(CloudDnsZoneSyncFailedEvent), Broker),
        ["TrustAnchorRollover"] = (typeof(TrustAnchorRolloverEvent), Broker),
        // Change events that carry ids and flags only.
        ["DnsFilterListChanged"] = (typeof(DnsFilterListChangedEvent), Broker),
        ["DnsFilterListEntryChanged"] = (typeof(DnsFilterListEntryChangedEvent), Broker),
        ["DnsForwarderChanged"] = (typeof(DnsForwarderChangedEvent), Broker),
        ["DnsRewriteChanged"] = (typeof(DnsRewriteChangedEvent), Broker),
        ["DnsAccessControlRuleChanged"] = (typeof(DnsAccessControlRuleChangedEvent), Broker),
        ["DhcpLeaseChanged"] = (typeof(DhcpLeaseChangedEvent), Broker),
        ["UserAccountChanged"] = (typeof(UserAccountChangedEvent), Broker),
        ["RoleChanged"] = (typeof(RoleChangedEvent), Broker),
        // Carries settings values in clear (secrets included): never crosses the broker.
        ["ClusterConfigSettingsChanged"] = (
            typeof(ClusterConfigSettingsChangedEvent),
            DnsServerEventDeliveryFlags.None
        ),
    };

    private static readonly Dictionary<Type, string> TypeToName = Entries.ToDictionary(
        pair => pair.Value.Type,
        pair => pair.Key
    );

    /// <summary>
    ///     This property lists every registered event type.
    /// </summary>
    public static IReadOnlyCollection<Type> RegisteredTypes { get; } =
    [.. Entries.Values.Select(entry => entry.Type)];

    /// <summary>
    ///     This method looks up the registered name for an event's runtime type.
    /// </summary>
    /// <param name="domainEvent">The event to look up a name for.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the event's type is not registered.
    /// </exception>
    public static string GetName(IDnsServerEvent domainEvent) =>
        TypeToName.TryGetValue(domainEvent.GetType(), out string name)
            ? name
            : throw new InvalidOperationException(
                $"Event type '{domainEvent.GetType()}' is not registered in {nameof(DnsServerEventTypeRegistry)}."
            );

    /// <summary>
    ///     This method looks up the event type registered under a name.
    /// </summary>
    /// <param name="eventTypeName">The registered name to look up.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="eventTypeName" /> is not registered, for example a message
    ///     from a newer Ninlil version.
    /// </exception>
    public static Type GetType(string eventTypeName) =>
        TryGetType(eventTypeName, out Type type)
            ? type
            : throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is not registered in {nameof(DnsServerEventTypeRegistry)}."
            );

    /// <summary>
    ///     This method looks up the event type registered under a name without throwing.
    /// </summary>
    /// <param name="eventTypeName">The registered name to look up; may be <see langword="null" />.</param>
    /// <param name="type">The registered type, or <see langword="null" />.</param>
    public static bool TryGetType(string eventTypeName, out Type type)
    {
        if (eventTypeName is not null && Entries.TryGetValue(eventTypeName, out var entry))
        {
            type = entry.Type;
            return true;
        }

        type = null;
        return false;
    }

    /// <summary>
    ///     This method looks up the registered name for an event type without throwing.
    /// </summary>
    /// <param name="eventType">The event's runtime type.</param>
    /// <param name="name">The registered name, or <see langword="null" />.</param>
    public static bool TryGetName(Type eventType, out string name) =>
        TypeToName.TryGetValue(eventType, out name);

    /// <summary>
    ///     This method returns the delivery flags of an event type, or
    ///     <see cref="DnsServerEventDeliveryFlags.None" /> for a type that is not registered.
    /// </summary>
    /// <param name="eventType">The event's runtime type.</param>
    public static DnsServerEventDeliveryFlags GetDeliveryFlags(Type eventType) =>
        TypeToName.TryGetValue(eventType, out string name)
            ? Entries[name].Flags
            : DnsServerEventDeliveryFlags.None;
}
