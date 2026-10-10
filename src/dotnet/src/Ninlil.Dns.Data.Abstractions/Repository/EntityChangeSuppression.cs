// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This class lets a writer say that the rows it is about to save are not a change any
///     in-process cache cares about, so <see cref="IEntityChangeNotifier" /> stays silent for those
///     entity types while the scope is open. The client presence flush uses it: it advances
///     <c>LastSeenUtc</c> on client addresses about every 30 seconds, and no cache is built from
///     that field, but every signal would make the next DNS query wait for a reload of the client
///     directory.
///     <para>
///         The scope is ambient (it flows with the async call chain of the code that opened it), so
///         it covers the saves made inside it and nothing made by other requests at the same time.
///         It suppresses the signal for every subscriber, so it is only for rows no cache reads.
///         The scope must also enclose the commit: a save inside an explicit transaction raises
///         its signal when the transaction commits.
///     </para>
/// </summary>
public static class EntityChangeSuppression
{
    /// <summary>
    ///     This field holds the entity types suppressed in the current async flow.
    /// </summary>
    private static readonly AsyncLocal<IReadOnlySet<Type>> Current = new();

    /// <summary>
    ///     This method opens a scope in which changes to the given entity types raise no signal.
    ///     Scopes nest; disposing one restores the types suppressed before it.
    /// </summary>
    /// <param name="entityTypes">The CLR types of the entities to keep quiet.</param>
    /// <returns>A handle that closes the scope when disposed.</returns>
    public static IDisposable Begin(params Type[] entityTypes)
    {
        IReadOnlySet<Type> previous = Current.Value;
        HashSet<Type> combined = previous is null ? [] : [.. previous];
        combined.UnionWith(entityTypes);
        Current.Value = combined;

        return new Scope(previous);
    }

    /// <summary>
    ///     This method says whether a change to an entity type is suppressed in the current async
    ///     flow.
    /// </summary>
    /// <param name="entityType">The CLR type of the changed entity.</param>
    public static bool IsSuppressed(Type entityType) =>
        Current.Value is { } suppressed && suppressed.Contains(entityType);

    /// <summary>
    ///     This class is the handle that restores the earlier set when disposed.
    /// </summary>
    private sealed class Scope(IReadOnlySet<Type> previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Current.Value = previous;
    }
}
