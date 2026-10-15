using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Webhooks.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class tests <see cref="WebhookEventCatalog" /> against a plain
///     <see cref="ServiceCollection" /> that <c>AddNinlilDnsServerWebhooks</c> filled, so the
///     catalog and the subscriber registrations cannot drift apart, and against the event type
///     registry, so every catalog name resolves.
/// </summary>
public sealed class WebhookEventCatalogTests
{
    [Fact]
    public void AddNinlilDnsServerWebhooks_RegistersOneSubscriberForEachCatalogTypeAndNoOther()
    {
        ServiceCollection services = new();
        services.AddNinlilDnsServerWebhooks(new ConfigurationBuilder().Build());

        List<Type> registeredEventTypes =
        [
            .. services
                .Where(descriptor =>
                    descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition()
                        == typeof(IDnsServerEventSubscriber<>)
                )
                .Select(descriptor =>
                {
                    Type eventType = descriptor.ServiceType.GetGenericArguments()[0];
                    Assert.Equal(
                        typeof(WebhookDeliverySubscriber<>).MakeGenericType(eventType),
                        descriptor.ImplementationType
                    );
                    return eventType;
                }),
        ];

        Assert.Equal(
            WebhookEventCatalog.EventTypes.OrderBy(type => type.Name),
            registeredEventTypes.OrderBy(type => type.Name)
        );
        Assert.Equal(7, registeredEventTypes.Count);
    }

    [Fact]
    public void EventTypeNames_AreTheRegistryNamesOfTheCatalogTypes()
    {
        foreach (Type type in WebhookEventCatalog.EventTypes)
        {
            Assert.True(DnsServerEventTypeRegistry.TryGetName(type, out string name));
            Assert.Contains(name, WebhookEventCatalog.EventTypeNames);
            Assert.True(WebhookEventCatalog.IsDeliverable(name));
        }

        Assert.Equal(
            WebhookEventCatalog.EventTypes.Count,
            WebhookEventCatalog.EventTypeNames.Count
        );
    }

    [Theory]
    [InlineData("DnsQueryReceived")]
    [InlineData("DnsQueryAnswered")]
    [InlineData("UserAccountChanged")]
    [InlineData("NotARealEventType")]
    [InlineData("dnszonechanged")]
    [InlineData("")]
    [InlineData(null)]
    public void IsDeliverable_ForANameOutsideTheCatalog_IsFalse(string name)
    {
        Assert.False(WebhookEventCatalog.IsDeliverable(name));
    }
}
