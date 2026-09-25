using Ninlil.Dns.Server.Core.Events;

namespace Ninlil.Dns.Server.Core.Tests.Fakes;

/// <summary>
///     This class records every event published to it, in order, instead of dispatching it, so
///     a test can assert on what was published.
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
