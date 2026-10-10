// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class records, per dependency-injection scope, whether the scope wants to read from
///     the primary database or from a replica. Storage resolves it once, when the scope first
///     resolves a database context (the Entity Framework Core <c>NinlilDnsContext</c> or the
///     MongoDB context): the context factory asks <see cref="IStorageReadRouter" /> and the
///     answer fixes the connection for the whole scope.
///     <para>
///         Code that only reads sets <see cref="StorageReadTargetEnum.Replica" /> before it
///         resolves any repository, usually through
///         <see cref="ServiceScopeFactoryReadExtension.CreateReplicaReadScope" />. Setting it
///         afterwards throws, so a mistake shows up in a test and not as a silent read from the
///         primary. The default is <see cref="StorageReadTargetEnum.Primary" />, which is what
///         every scope that never sets it gets.
///     </para>
/// </summary>
public sealed class StorageReadIntent
{
    private readonly object gate = new();
    private bool? useReplica;

    /// <summary>
    ///     This property contains the target the scope asked for. It is
    ///     <see cref="StorageReadTargetEnum.Primary" /> until <see cref="Set" /> changes it.
    /// </summary>
    public StorageReadTargetEnum Target { get; private set; } = StorageReadTargetEnum.Primary;

    /// <summary>
    ///     This property reports whether a database context has been resolved in the scope, which
    ///     fixes the intent.
    /// </summary>
    public bool IsResolved
    {
        get
        {
            lock (gate)
                return useReplica.HasValue;
        }
    }

    /// <summary>
    ///     This method sets the target the scope wants to read from.
    /// </summary>
    /// <param name="target">The target to ask for.</param>
    /// <exception cref="InvalidOperationException">
    ///     A database context was already resolved in the scope, so the connection is fixed.
    /// </exception>
    public void Set(StorageReadTargetEnum target)
    {
        lock (gate)
        {
            if (useReplica.HasValue)
                throw new InvalidOperationException(
                    "The storage read intent cannot change after the scope resolved a repository: "
                        + "set the read intent before resolving a repository."
                );

            Target = target;
        }
    }

    /// <summary>
    ///     This method fixes the intent and says whether the scope reads from a replica. The
    ///     first call asks <paramref name="router" />; later calls return the same answer, so
    ///     every component in the scope agrees on the connection.
    /// </summary>
    /// <param name="router">The router that decides whether a replica may be used.</param>
    /// <param name="principalKey">
    ///     The key of the user the scope works for, or null for background work. The router uses
    ///     it to keep a principal that just wrote on the primary.
    /// </param>
    /// <returns>True when the scope reads from a replica.</returns>
    public bool ResolveUseReplica(IStorageReadRouter router, string principalKey = null)
    {
        lock (gate)
        {
            useReplica ??= router.UseReplica(this, principalKey);
            return useReplica.Value;
        }
    }
}
