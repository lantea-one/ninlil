// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class maps every broker-eligible <see cref="IDnsServerEvent" /> concrete type to (and
///     from) a short, stable, explicit name -- used as <see cref="DnsServerEventEnvelope.EventTypeName" />
///     -- rather than a reflection-derived <see cref="Type.AssemblyQualifiedName" /> or
///     <see cref="Type.FullName" />. The same "explicit registry, not reflection-derived name"
///     choice <see cref="Common.Wire.Records.DnsRecordDataCodecRegistry" /> and this project's
///     own <c>DnssecAlgorithmRegistry</c> already make elsewhere: an explicit name survives a
///     class rename/namespace move that would otherwise
///     silently break every already-queued or already-persisted broker message referencing the
///     old reflected name.
/// </summary>
public static class DnsServerEventTypeRegistry
{
    private static readonly Dictionary<string, Type> NameToType = new()
    {
        ["DnsQueryReceived"] = typeof(DnsQueryReceivedEvent),
        ["DnsQueryAnswered"] = typeof(DnsQueryAnsweredEvent),
        ["DnsZoneChanged"] = typeof(DnsZoneChangedEvent),
        ["DnsCacheEvicted"] = typeof(DnsCacheEvictedEvent),

        // Four genuinely noteworthy moments, curated as webhook-eligible subscription targets
        // alongside the two above.
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
    ///     This method looks up the registered name for an event's own concrete runtime type.
    /// </summary>
    /// <param name="domainEvent">The event to look up a name for.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="domainEvent" />'s own concrete type is not registered --
    ///     every <see cref="IDnsServerEvent" /> that should ever cross a broker must be added to
    ///     this registry.
    /// </exception>
    public static string GetName(IDnsServerEvent domainEvent) =>
        TypeToName.TryGetValue(domainEvent.GetType(), out string name)
            ? name
            : throw new InvalidOperationException(
                $"Event type '{domainEvent.GetType()}' is not registered in {nameof(DnsServerEventTypeRegistry)} and cannot cross a message broker."
            );

    /// <summary>
    ///     This method looks up the registered concrete type for an event's own name.
    /// </summary>
    /// <param name="eventTypeName">The registered name to look up.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="eventTypeName" /> is not registered -- most likely a
    ///     message from a newer Ninlil version this instance's own registry does not yet
    ///     know about.
    /// </exception>
    public static Type GetType(string eventTypeName) =>
        NameToType.TryGetValue(eventTypeName, out Type type)
            ? type
            : throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is not registered in {nameof(DnsServerEventTypeRegistry)}."
            );
}
