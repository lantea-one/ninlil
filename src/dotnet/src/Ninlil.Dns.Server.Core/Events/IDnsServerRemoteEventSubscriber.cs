// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This marker interface flags an <see cref="IDnsServerEventSubscriber{TEvent}" /> that also
///     handles events other Ninlil instances published through the message broker. Every other
///     subscriber sees only this process's own events, because each node has its own database:
///     a foreign zone, lease, user or rule id means nothing here, and query logging, webhooks,
///     NOTIFY, PTR, CAA, catalog work and cluster broadcasts are the producing instance's job.
///     <see cref="DnsServerEventDispatcher" /> reads it.
/// </summary>
public interface IDnsServerRemoteEventSubscriber;
