using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class makes a <c>NinlilDnsContext</c> that reads from a replica refuse to write. The
///     <see cref="Extension.ServiceCollectionEntityFrameworkCoreExtension" /> attaches it to
///     exactly the contexts it points at the read connection. It throws
///     <see cref="StorageReadOnlyException" /> for <c>SaveChanges</c>, for every non-query command
///     (<c>ExecuteDelete</c>, <c>ExecuteUpdate</c>, raw SQL) and for every other statement than a
///     <c>SELECT</c> or <c>WITH</c> query, so a mistake fails loudly in a test instead of as a
///     provider error ("cannot execute in a read-only transaction") on a live replica. Code that
///     builds its own command on the context's connection bypasses these hooks and checks
///     <see cref="NinlilDnsContext.IsReplicaView" /> itself.
/// </summary>
public sealed class ReadOnlyGuardInterceptor : SaveChangesInterceptor, IDbCommandInterceptor
{
    /// <summary>
    ///     This property contains the shared instance; the interceptor holds no state.
    /// </summary>
    public static ReadOnlyGuardInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    ) => throw StorageReadOnlyException.For("SaveChanges");

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken stoppingToken = default
    ) => throw StorageReadOnlyException.For("SaveChanges");

    /// <inheritdoc />
    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        RequireQuery(command);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken stoppingToken = default
    )
    {
        RequireQuery(command);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    /// <inheritdoc />
    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        RequireQuery(command);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken stoppingToken = default
    )
    {
        RequireQuery(command);
        return new ValueTask<InterceptionResult<object>>(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    ) => throw StorageReadOnlyException.For("A non-query command");

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken stoppingToken = default
    ) => throw StorageReadOnlyException.For("A non-query command");

    /// <summary>
    ///     This method throws unless the command text starts with <c>SELECT</c> or <c>WITH</c>,
    ///     ignoring leading white space.
    /// </summary>
    private static void RequireQuery(DbCommand command)
    {
        ReadOnlySpan<char> text = command.CommandText.AsSpan().TrimStart();
        if (
            text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
        )
            return;

        throw StorageReadOnlyException.For("A statement that is not a query");
    }
}
