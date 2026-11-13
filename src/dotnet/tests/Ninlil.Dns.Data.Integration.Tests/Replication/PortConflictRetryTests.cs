using Ninlil.Dns.Data.EngineHosts;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class covers <see cref="ReplicatedEngineHost.RetryOnPortConflictAsync" /> and
///     <see cref="ReplicatedEngineHost.IsPortConflict" />, the retry that the MongoDB replica set
///     host uses when Docker finds a reserved host port taken. It uses fake attempts that throw
///     Docker's message, so it needs no container and no real collision.
/// </summary>
public sealed class PortConflictRetryTests
{
    private const string DockerMessage =
        "failed to bind host port 0.0.0.0:34131/tcp: address already in use";

    /// <summary>
    ///     This test checks that a port conflict costs a retry: cleanup runs and the next
    ///     attempt succeeds.
    /// </summary>
    [Fact]
    public async Task Retry_WhenTheFirstAttemptsFindAPortTaken_RunsAgainAfterCleanup()
    {
        List<int> attempts = [];
        int cleanups = 0;

        await ReplicatedEngineHost.RetryOnPortConflictAsync(
            number =>
            {
                attempts.Add(number);
                return number < 2
                    ? Task.FromException(new InvalidOperationException(DockerMessage))
                    : Task.CompletedTask;
            },
            () =>
            {
                cleanups++;
                return Task.CompletedTask;
            }
        );

        Assert.Equal([0, 1, 2], attempts);
        Assert.Equal(2, cleanups);
    }

    /// <summary>
    ///     This test checks that a failure that is not a port conflict surfaces at once.
    /// </summary>
    [Fact]
    public async Task Retry_WhenTheFailureIsAnotherError_ThrowsWithoutRetrying()
    {
        int attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReplicatedEngineHost.RetryOnPortConflictAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new InvalidOperationException("no such image"));
                },
                () => Task.CompletedTask
            )
        );

        Assert.Equal(1, attempts);
    }

    /// <summary>
    ///     This test checks that the retry is bounded: a conflict on every attempt throws the
    ///     last error after <c>maxAttempts</c> tries.
    /// </summary>
    [Fact]
    public async Task Retry_WhenEveryAttemptFindsAPortTaken_ThrowsAfterTheBound()
    {
        int attempts = 0;

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReplicatedEngineHost.RetryOnPortConflictAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new InvalidOperationException(DockerMessage));
                },
                () => Task.CompletedTask,
                maxAttempts: 3
            )
        );

        Assert.Equal(3, attempts);
        Assert.Contains("address already in use", thrown.Message);
    }

    /// <summary>
    ///     This test checks that the conflict is found inside a wrapping exception.
    /// </summary>
    [Fact]
    public void IsPortConflict_WhenTheMessageIsInAnInnerException_ReturnsTrue()
    {
        Exception wrapped = new AggregateException(
            "start failed",
            new InvalidOperationException(DockerMessage)
        );

        Assert.True(ReplicatedEngineHost.IsPortConflict(wrapped));
        Assert.False(ReplicatedEngineHost.IsPortConflict(new InvalidOperationException("boom")));
    }
}
