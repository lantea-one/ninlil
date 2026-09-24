// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface is implemented by every event Ninlil's DNS server publishes internally
///     -- a query answered, a zone changed, a cache entry evicted -- so that cross-cutting
///     concerns like metrics recording and structured logging can subscribe to what happened
///     without the query-answering pipeline itself knowing, or caring, that they exist. This
///     keeps a metrics write or a database log entry from ever sitting in the same call stack as
///     answering a query, which matters at DNS's query volumes: a synchronous database write on
///     every single query would cap this server's throughput far below what it is otherwise
///     capable of.
/// </summary>
public interface IDnsServerEvent
{
    /// <summary>
    ///     This property contains the UTC date and time the event occurred.
    /// </summary>
    DateTimeOffset OccurredAtUtc { get; }
}
