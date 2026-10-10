// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class records, per dependency-injection scope, which user the scope works for and
///     whether the scope has written. It is how read-your-own-writes reaches the storage layer:
///     the HTTP middleware sets <see cref="Key" /> from the authenticated user, the storage layer
///     calls <see cref="ReportWrite" /> after each save, and <see cref="IStorageReadRouter" />
///     keeps that user's next reads on the primary for the sticky window
///     (<c>Storage:ReadReplica:StickyAfterWriteSeconds</c>). The context factory also passes
///     <see cref="Key" /> to <see cref="IStorageReadRouter.UseReplica" /> when a scope that asked
///     for the replica resolves its first context.
///     <para>
///         Background scopes never set a key, so their writes are not attributed to anyone.
///     </para>
/// </summary>
/// <param name="router">The router told about the writes of the principal.</param>
public sealed class StoragePrincipal(IStorageReadRouter router)
{
    private volatile string key;
    private volatile bool hasWritten;

    /// <summary>
    ///     This property contains the key of the user the scope works for, or null when the scope
    ///     has no user (background work, an anonymous request).
    /// </summary>
    public string Key => key;

    /// <summary>
    ///     This property reports whether a save succeeded in the scope.
    /// </summary>
    public bool HasWritten => hasWritten;

    /// <summary>
    ///     This method builds the key that identifies a user, whatever credential the user
    ///     presented (a JWT or an API token), so a write through one is seen by reads through the
    ///     other.
    /// </summary>
    /// <param name="userId">The identifier of the authenticated user.</param>
    /// <returns>The principal key.</returns>
    public static string KeyForUser(Guid userId) => "user:" + userId.ToString("N");

    /// <summary>
    ///     This method sets the user the scope works for. The middleware calls it before the
    ///     endpoint runs.
    /// </summary>
    /// <param name="principalKey">The key, usually from <see cref="KeyForUser" />.</param>
    public void Set(string principalKey) => key = principalKey;

    /// <summary>
    ///     This method records that a save succeeded in the scope. With a user, the router starts
    ///     (or restarts) that user's sticky window. A save inside a transaction calls this before
    ///     the commit; <see cref="Restamp" /> repeats it when the request ends.
    /// </summary>
    public void ReportWrite()
    {
        hasWritten = true;

        string principalKey = key;
        if (principalKey != null)
            router.RecordWrite(principalKey);
    }

    /// <summary>
    ///     This method starts the sticky window again from now when the scope wrote, so a request
    ///     that took long, or committed late, still protects the user's next read. It does
    ///     nothing for a scope that did not write.
    /// </summary>
    public void Restamp()
    {
        if (hasWritten)
            ReportWrite();
    }
}
