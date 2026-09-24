using Ninlil.Dns.Server.Core.Events;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class captures every event published to it, in order, instead of actually
///     dispatching them anywhere -- enough to assert a cache eviction was reported without
///     standing up the real <see cref="DnsServerEventDispatcher" />.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent) => Published.Add(domainEvent);
}
