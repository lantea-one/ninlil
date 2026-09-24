using Ninlil.Dns.Server.Core.Events;

namespace Ninlil.Dns.Server.Forwarding.Tests.Fakes;

/// <summary>
///     This class captures every event published to it instead of dispatching it anywhere.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent)
    {
        lock (Published)
            Published.Add(domainEvent);
    }
}
