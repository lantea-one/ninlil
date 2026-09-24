// Define our namespace.
namespace Ninlil.Common.Extension.Enumerable;

/// <summary>
///     This class provides extension methods for awaiting all tasks in an enumerable collection of tasks.
/// </summary>
public static class AwaitAllEnumerableExtension
{
    /// <summary>
    ///     This extension method allows you to await all tasks in an enumerable collection of tasks.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">Optional <see cref="CancellationToken"/> to cancel the awaiting of tasks.</param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable <see cref="Task{Array}"/> that resolves to an
    ///     array of <typeparamref name="TValue"/> when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is cancelled.
    /// </exception>
    public static Task<TValue[]> AwaitAll<TValue>(
        this IEnumerable<Task<TValue>> instance,
        CancellationToken stoppingToken = default
    )
    {
        // Ensure cancellation is not requested before starting the tasks.
        stoppingToken.ThrowIfCancellationRequested();

        // We're done, return the awaited tasks.
        return Task.WhenAll(instance);
    }

    /// <summary>
    ///     This extension method allows you to await all tasks in an enumerable collection of tasks.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">Optional <see cref="CancellationToken"/> to cancel the awaiting of tasks.</param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable <see cref="Task{List}"/> that resolves to a <see cref="List{TValue}"/> when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is cancelled.
    /// </exception>
    public static Task<IEnumerable<TValue>> AwaitAllEnumerable<TValue>(
        this IEnumerable<Task<TValue>> instance,
        CancellationToken stoppingToken = default
    ) =>
        AwaitAllList(instance, stoppingToken)
            .ContinueWith(
                t => t.Result ?? new List<TValue>() as IEnumerable<TValue>,
                stoppingToken
            );

    /// <summary>
    ///     This extension method allows you to await all tasks in an enumerable collection of tasks.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">Optional <see cref="CancellationToken"/> to cancel the awaiting of tasks.</param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable <see cref="Task{List}"/> that resolves to a <see cref="List{TValue}"/> when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is cancelled.
    /// </exception>
    public static Task<List<TValue>> AwaitAllList<TValue>(
        this IEnumerable<Task<TValue>> instance,
        CancellationToken stoppingToken = default
    ) =>
        AwaitAll(instance, stoppingToken)
            .ContinueWith(t => t.Result?.ToList() ?? [], stoppingToken);
}
