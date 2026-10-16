using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IForwarderHostResolver" /> a test drives by hand: every call is
///     logged and stays pending until the test completes or fails it, so the tests decide when a
///     background lookup finishes and no real DNS lookup ever happens.
/// </summary>
public sealed class FakeForwarderHostResolver : IForwarderHostResolver
{
    private readonly List<(string Host, TaskCompletionSource<IPAddress[]> Source)> _calls = [];

    /// <summary>The host names asked for so far, in call order.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
                return [.. _calls.Select(call => call.Host)];
        }
    }

    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken stoppingToken)
    {
        // Asynchronous continuations keep a completing test thread from running the cache's code
        // inline, which would hide an ordering mistake.
        TaskCompletionSource<IPAddress[]> source = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        lock (_calls)
            _calls.Add((host, source));

        return source.Task;
    }

    /// <summary>
    ///     This method answers the call with that index.
    /// </summary>
    public void Complete(int callIndex, params IPAddress[] addresses)
    {
        lock (_calls)
            _calls[callIndex].Source.SetResult(addresses);
    }

    /// <summary>
    ///     This method fails the call with that index, as a resolver does for an unknown host.
    /// </summary>
    public void Fail(int callIndex)
    {
        lock (_calls)
            _calls[callIndex].Source.SetException(new SocketException(11001));
    }
}
