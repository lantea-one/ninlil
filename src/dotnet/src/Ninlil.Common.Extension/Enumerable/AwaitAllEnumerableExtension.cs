// Define our namespace.
namespace Ninlil.Common.Extension.Enumerable;

/// <summary>
///     This static class awaits a sequence of tasks together and returns their results as an
///     array, list or enumerable. It wraps <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})" />.
/// </summary>
public static class AwaitAllEnumerableExtension
{
    /// <summary>
    ///     This method awaits every task in <paramref name="instance" />.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">
    ///     Optional <see cref="CancellationToken"/>. It is only checked before waiting starts; it does
    ///     not stop the wait.
    /// </param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable <see cref="Task{Array}"/> that resolves to an
    ///     array of <typeparamref name="TValue"/> when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is already cancelled.
    /// </exception>
    public static Task<TValue[]> AwaitAll<TValue>(
        this IEnumerable<Task<TValue>> instance,
        CancellationToken stoppingToken = default
    )
    {
        // Ensure cancellation is not requested before starting the tasks.
        stoppingToken.ThrowIfCancellationRequested();

        // We're done, return the combined task.
        return Task.WhenAll(instance);
    }

    /// <summary>
    ///     This method awaits every task in <paramref name="instance" /> and returns the results as
    ///     an <see cref="IEnumerable{T}" />.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">Optional <see cref="CancellationToken"/> checked before waiting starts.</param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable task that resolves to the results when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is already cancelled.
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
    ///     This method awaits every task in <paramref name="instance" /> and returns the results as a
    ///     list.
    /// </summary>
    /// <param name="instance">The <see cref="IEnumerable{Task}"/> instance to await.</param>
    /// <param name="stoppingToken">Optional <see cref="CancellationToken"/> checked before waiting starts.</param>
    /// <typeparam name="TValue">The type of the value returned by the tasks.</typeparam>
    /// <returns>
    ///     An awaitable <see cref="Task{List}"/> that resolves to a <see cref="List{TValue}"/> when all tasks complete.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    ///     When the <paramref name="stoppingToken"/> is already cancelled.
    /// </exception>
    public static Task<List<TValue>> AwaitAllList<TValue>(
        this IEnumerable<Task<TValue>> instance,
        CancellationToken stoppingToken = default
    ) =>
        AwaitAll(instance, stoppingToken)
            .ContinueWith(t => t.Result?.ToList() ?? [], stoppingToken);
}
