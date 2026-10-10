using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests <see cref="EntityChangeSuppression" /> against the real
///     <see cref="EntityChangeNotifier" />: a suppressed type raises no callback while its scope is
///     open (also across an await), other types in the same signal still arrive, scopes nest and
///     restore the outer set, and a scope on one async flow does not silence another. The
///     presence flush uses the scope so its saves do not reload the client directory.
/// </summary>
public sealed class EntityChangeSuppressionTests
{
    private readonly EntityChangeNotifier _notifier = new();

    private readonly List<Type> _received = [];

    public EntityChangeSuppressionTests() =>
        _notifier.Subscribe([], types => _received.AddRange(types));

    [Fact]
    public void NotifyChanged_ForASuppressedType_RaisesNothingUntilTheScopeIsDisposed()
    {
        using (EntityChangeSuppression.Begin(typeof(string)))
            _notifier.NotifyChanged([typeof(string)]);

        Assert.Empty(_received);

        _notifier.NotifyChanged([typeof(string)]);

        Assert.Equal([typeof(string)], _received);
    }

    [Fact]
    public void NotifyChanged_ForSuppressedAndOtherTypes_DeliversOnlyTheOthers()
    {
        using (EntityChangeSuppression.Begin(typeof(string)))
            _notifier.NotifyChanged([typeof(string), typeof(int)]);

        Assert.Equal([typeof(int)], _received);
    }

    [Fact]
    public void Begin_NestedScopes_RestoreTheOuterSetOnDispose()
    {
        using (EntityChangeSuppression.Begin(typeof(string)))
        {
            using (EntityChangeSuppression.Begin(typeof(int)))
            {
                _notifier.NotifyChanged([typeof(string), typeof(int)]);
                Assert.Empty(_received);
            }

            _notifier.NotifyChanged([typeof(string), typeof(int)]);
        }

        Assert.Equal([typeof(int)], _received);
    }

    [Fact]
    public async Task NotifyChanged_AfterAnAwaitInsideTheScope_IsStillSuppressed()
    {
        using (EntityChangeSuppression.Begin(typeof(string)))
        {
            await Task.Delay(10);
            _notifier.NotifyChanged([typeof(string)]);
        }

        Assert.Empty(_received);
    }

    [Fact]
    public async Task NotifyChanged_OnAFlowStartedBeforeTheScope_IsNotSuppressed()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // This flow captured its context before the scope opens, so the scope never reaches it.
        Task other = Task.Run(async () =>
        {
            await gate.Task;
            _notifier.NotifyChanged([typeof(string)]);
        });

        using (EntityChangeSuppression.Begin(typeof(string)))
        {
            gate.SetResult();
            await other;
        }

        Assert.Equal([typeof(string)], _received);
    }
}
