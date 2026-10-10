// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This class is the one in-process implementation of <see cref="IEntityChangeNotifier" />.
///     Both storage engines register it as a singleton. Subscriptions live in an immutable array
///     that is replaced as a whole under a lock, so <see cref="NotifyChanged" /> (a hot path: every
///     save calls it) reads a snapshot without locking.
/// </summary>
/// <param name="onCallbackError">
///     Called with the exception of a callback that threw, so the host can log it; may be
///     <see langword="null" />, which ignores the exception. The save that raised the signal never
///     sees the exception.
/// </param>
public sealed class EntityChangeNotifier(Action<Exception> onCallbackError = null)
    : IEntityChangeNotifier
{
    /// <summary>
    ///     This field guards writes to <see cref="_subscriptions" />.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     This field is the current subscription list. It is replaced, never changed in place.
    /// </summary>
    private volatile Subscription[] _subscriptions = [];

    /// <inheritdoc />
    public void NotifyChanged(IReadOnlyCollection<Type> entityTypes)
    {
        if (entityTypes is null || entityTypes.Count == 0)
            return;

        foreach (Subscription subscription in _subscriptions)
        {
            IReadOnlyCollection<Type> matching = subscription.Match(entityTypes);

            if (matching.Count == 0)
                continue;

            try
            {
                subscription.Callback(matching);
            }
            catch (Exception exception)
            {
                try
                {
                    onCallbackError?.Invoke(exception);
                }
                catch
                {
                    // A failing error handler must not turn into a failed save.
                }
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(
        IReadOnlyCollection<Type> entityTypes,
        Action<IReadOnlyCollection<Type>> callback
    )
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        ArgumentNullException.ThrowIfNull(callback);

        Subscription subscription = new(this, [.. entityTypes], callback);

        lock (_gate)
            _subscriptions = [.. _subscriptions, subscription];

        return subscription;
    }

    /// <summary>
    ///     This method removes a subscription from the list.
    /// </summary>
    private void Remove(Subscription subscription)
    {
        lock (_gate)
            _subscriptions = [.. _subscriptions.Where(item => item != subscription)];
    }

    /// <summary>
    ///     This class is one registered callback and the handle that removes it.
    /// </summary>
    private sealed class Subscription(
        EntityChangeNotifier owner,
        HashSet<Type> types,
        Action<IReadOnlyCollection<Type>> callback
    ) : IDisposable
    {
        /// <summary>
        ///     This property is the callback to run.
        /// </summary>
        public Action<IReadOnlyCollection<Type>> Callback { get; } = callback;

        /// <summary>
        ///     This method returns the changed types this subscription watches.
        /// </summary>
        public IReadOnlyCollection<Type> Match(IReadOnlyCollection<Type> changed) =>
            types.Count == 0 ? changed : [.. changed.Where(types.Contains)];

        /// <inheritdoc />
        public void Dispose() => owner.Remove(this);
    }
}
