// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface is the signal that tells in-memory caches which kinds of entities were
///     written. A storage engine calls <see cref="NotifyChanged" /> after a save has committed,
///     and a cache calls <see cref="Subscribe" /> for the entity types it was built from. The
///     cache then marks its copy out of date (for example
///     <c>RefreshingSnapshot&lt;T&gt;.Expire</c>), so a write made through the API or any other
///     repository is visible to the next read in this process without each writer knowing which
///     caches exist. It reports local writes only: a write made by another cluster node or a
///     process outside this one does not raise it, which is why caches also keep a time-to-live.
///     <para>
///         The entity-framework engine raises it from a save interceptor, and the MongoDB engine
///         raises it from the unit of work; both register one singleton implementation.
///     </para>
/// </summary>
public interface IEntityChangeNotifier
{
    /// <summary>
    ///     This method reports that rows of the given entity types were added, changed or deleted
    ///     and are now committed. Callbacks run synchronously on the calling thread, one failing
    ///     callback does not stop the others, and the exception is not rethrown.
    /// </summary>
    /// <param name="entityTypes">The CLR types of the entities that changed.</param>
    void NotifyChanged(IReadOnlyCollection<Type> entityTypes);

    /// <summary>
    ///     This method registers a callback for changes to any of the given entity types.
    /// </summary>
    /// <param name="entityTypes">
    ///     The types to watch. An empty collection watches every type, which a cache uses when it
    ///     is built from rows of many kinds.
    /// </param>
    /// <param name="callback">
    ///     Called with the watched types that changed in one save. It runs on the thread that
    ///     saved, inside the request, so it must only mark state and return; it must not query
    ///     the database or wait.
    /// </param>
    /// <returns>A handle that removes the subscription when disposed.</returns>
    IDisposable Subscribe(
        IReadOnlyCollection<Type> entityTypes,
        Action<IReadOnlyCollection<Type>> callback
    );
}
