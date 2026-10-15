using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.EventBus;

/// <summary>
///     This class checks <see cref="DnsServerEventTypeRegistry" /> by reflection over the Core
///     assembly: every concrete event type must be classified, so a new event class cannot skip
///     the registry.
/// </summary>
public class DnsServerEventTypeRegistryTests
{
    private static IEnumerable<Type> AllEventTypes() =>
        typeof(IDnsServerEvent)
            .Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract
                && !type.IsInterface
                && typeof(IDnsServerEvent).IsAssignableFrom(type)
            );

    [Fact]
    public void RegisteredTypes_CoverEveryConcreteEventType()
    {
        string[] missing =
        [
            .. AllEventTypes()
                .Where(type => !DnsServerEventTypeRegistry.TryGetName(type, out _))
                .Select(type => type.Name),
        ];

        Assert.Empty(missing);
        Assert.Equal(AllEventTypes().Count(), DnsServerEventTypeRegistry.RegisteredTypes.Count);
    }

    [Fact]
    public void GetType_OfEveryName_RoundTripsAndNamesAreUnique()
    {
        List<string> names = [];

        foreach (Type type in AllEventTypes())
        {
            Assert.True(DnsServerEventTypeRegistry.TryGetName(type, out string name), type.Name);
            Assert.Equal(type, DnsServerEventTypeRegistry.GetType(name));
            names.Add(name);
        }

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("DnsQueryReceived", typeof(DnsQueryReceivedEvent))]
    [InlineData("DnsQueryAnswered", typeof(DnsQueryAnsweredEvent))]
    [InlineData("DnsZoneChanged", typeof(DnsZoneChangedEvent))]
    [InlineData("DnsCacheEvicted", typeof(DnsCacheEvictedEvent))]
    [InlineData("DnssecZoneResigningCompleted", typeof(DnssecZoneResigningCompletedEvent))]
    [InlineData("AcmeCertificateRenewalSucceeded", typeof(AcmeCertificateRenewalSucceededEvent))]
    [InlineData("AcmeCertificateRenewalFailed", typeof(AcmeCertificateRenewalFailedEvent))]
    [InlineData("CloudDnsZoneSyncFailed", typeof(CloudDnsZoneSyncFailedEvent))]
    [InlineData("TrustAnchorRollover", typeof(TrustAnchorRolloverEvent))]
    public void GetType_OfAnExistingName_IsUnchanged(string name, Type expected) =>
        Assert.Equal(expected, DnsServerEventTypeRegistry.GetType(name));

    [Fact]
    public void ClusterConfigSettingsChanged_NeverCrossesTheBroker() =>
        Assert.False(
            DnsServerEventTypeRegistry
                .GetDeliveryFlags(typeof(ClusterConfigSettingsChangedEvent))
                .HasFlag(DnsServerEventDeliveryFlags.CrossesBroker)
        );

    [Fact]
    public void OnlyTheTwoQueryEvents_AreLossy()
    {
        string[] lossy =
        [
            .. AllEventTypes()
                .Where(type =>
                    DnsServerEventTypeRegistry
                        .GetDeliveryFlags(type)
                        .HasFlag(DnsServerEventDeliveryFlags.Lossy)
                )
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(DnsQueryAnsweredEvent), nameof(DnsQueryReceivedEvent)], lossy);
    }

    [Fact]
    public void GetDeliveryFlags_OfAnUnknownType_IsNone() =>
        Assert.Equal(
            DnsServerEventDeliveryFlags.None,
            DnsServerEventTypeRegistry.GetDeliveryFlags(typeof(string))
        );
}
