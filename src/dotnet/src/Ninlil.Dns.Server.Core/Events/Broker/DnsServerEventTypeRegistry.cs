// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class maps event types to short, stable names, used as
///     <see cref="DnsServerEventEnvelope.EventTypeName" /> and as webhook subscription event
///     names. Explicit names, rather than <see cref="Type.FullName" />, survive a class rename
///     or namespace move without breaking queued broker messages or stored webhook
///     subscriptions.
///     <para>
///         Only the types listed here can cross a broker. The other events (filter list,
///         forwarder, rewrite, access-control, DHCP lease, role, user and cluster settings
///         changes) are not registered, so with a broker transport
///         <see cref="DnsServerEventEnvelopeCodec.Encode" /> throws for them and they are not
///         delivered at all.
///     </para>
/// </summary>
public static class DnsServerEventTypeRegistry
{
    private static readonly Dictionary<string, Type> NameToType = new()
    {
        ["DnsQueryReceived"] = typeof(DnsQueryReceivedEvent),
        ["DnsQueryAnswered"] = typeof(DnsQueryAnsweredEvent),
        ["DnsZoneChanged"] = typeof(DnsZoneChangedEvent),
        ["DnsCacheEvicted"] = typeof(DnsCacheEvictedEvent),

        // Events chosen as webhook subscription targets, alongside the ones above.
        ["DnssecZoneResigningCompleted"] = typeof(DnssecZoneResigningCompletedEvent),
        ["AcmeCertificateRenewalSucceeded"] = typeof(AcmeCertificateRenewalSucceededEvent),
        ["AcmeCertificateRenewalFailed"] = typeof(AcmeCertificateRenewalFailedEvent),
        ["CloudDnsZoneSyncFailed"] = typeof(CloudDnsZoneSyncFailedEvent),
        ["TrustAnchorRollover"] = typeof(TrustAnchorRolloverEvent),
    };

    private static readonly Dictionary<Type, string> TypeToName = NameToType.ToDictionary(
        pair => pair.Value,
        pair => pair.Key
    );

    /// <summary>
    ///     This method looks up the registered name for an event's runtime type.
    /// </summary>
    /// <param name="domainEvent">The event to look up a name for.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the event's type is not registered. Any event that must cross a broker has
    ///     to be added here.
    /// </exception>
    public static string GetName(IDnsServerEvent domainEvent) =>
        TypeToName.TryGetValue(domainEvent.GetType(), out string name)
            ? name
            : throw new InvalidOperationException(
                $"Event type '{domainEvent.GetType()}' is not registered in {nameof(DnsServerEventTypeRegistry)} and cannot cross a message broker."
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
        NameToType.TryGetValue(eventTypeName, out Type type)
            ? type
            : throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is not registered in {nameof(DnsServerEventTypeRegistry)}."
            );
}
