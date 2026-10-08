using System.Threading.Channels;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class is the bounded hand-off between <see cref="DnsQueryLogSubscriber" /> (the
///     producer, running on the shared event dispatcher) and <see cref="DnsQueryLogWriterService" />
///     (the single consumer, which writes batches to the database). It is a singleton.
///     <para>
///         The queue holds at most <see cref="Capacity" /> entries (10 000 unless a test sets
///         it). <see cref="TryEnqueue" /> never waits: a full queue means the database cannot keep
///         up, and blocking would stall the dispatcher and every other event subscriber. The new
///         entry is dropped and counted in <see cref="DroppedCount" /> instead, so DNS keeps
///         answering and the log is incomplete only while the overload lasts. The writer reports
///         the count in the log.
///     </para>
/// </summary>
public sealed class DnsQueryLogQueue
{
    /// <summary>
    ///     This constant is the number of entries the queue holds in production.
    /// </summary>
    public const int DefaultCapacity = 10_000;

    private readonly Channel<DnsQueryLogEntryEntity> _channel;
    private long _droppedCount;

    /// <summary>
    ///     This constructor creates a queue of <see cref="DefaultCapacity" /> entries. The
    ///     dependency injection container uses it.
    /// </summary>
    public DnsQueryLogQueue()
        : this(DefaultCapacity) { }

    /// <summary>
    ///     This constructor creates a queue of <paramref name="capacity" /> entries, so tests can
    ///     fill it without queuing thousands of entries.
    /// </summary>
    /// <param name="capacity">The number of entries the queue holds. It must be positive.</param>
    internal DnsQueryLogQueue(int capacity)
    {
        Capacity = capacity;

        // Wait is the full mode, but writers use TryWrite only (see TryEnqueue): DropWrite would
        // drop silently and could not tell us how many entries were lost.
        _channel = Channel.CreateBounded<DnsQueryLogEntryEntity>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            }
        );
    }

    /// <summary>
    ///     This property contains the number of entries the queue holds.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    ///     This property contains how many entries were dropped because the queue was full, since
    ///     the process started.
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    ///     This property is the consumer side of the queue, for
    ///     <see cref="DnsQueryLogWriterService" />.
    /// </summary>
    internal ChannelReader<DnsQueryLogEntryEntity> Reader => _channel.Reader;

    /// <summary>
    ///     This method adds <paramref name="entry" /> without waiting.
    /// </summary>
    /// <param name="entry">The entry to write.</param>
    /// <returns>
    ///     <see langword="true" /> if the entry was queued, or <see langword="false" /> if the
    ///     queue was full and the entry was dropped and counted.
    /// </returns>
    public bool TryEnqueue(DnsQueryLogEntryEntity entry)
    {
        if (_channel.Writer.TryWrite(entry))
            return true;

        Interlocked.Increment(ref _droppedCount);
        return false;
    }
}
