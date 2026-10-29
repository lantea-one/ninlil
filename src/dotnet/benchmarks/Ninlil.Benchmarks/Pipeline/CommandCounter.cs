using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class is an EF Core interceptor that counts every database command the context runs,
///     so the pipeline benchmarks can prove in their setup that a warm query reads the database
///     zero times. It mirrors the interceptor of the same name in the forwarding tests.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    /// <summary>
    ///     This property is the number of commands executed so far.
    /// </summary>
    public int Count => Volatile.Read(ref _count);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken stoppingToken = default
    )
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, stoppingToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken stoppingToken = default
    )
    {
        Interlocked.Increment(ref _count);
        return base.NonQueryExecutingAsync(command, eventData, result, stoppingToken);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken stoppingToken = default
    )
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecutingAsync(command, eventData, result, stoppingToken);
    }
}
