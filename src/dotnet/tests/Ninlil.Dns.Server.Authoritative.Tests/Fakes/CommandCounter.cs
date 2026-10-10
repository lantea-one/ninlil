using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Fakes;

/// <summary>
///     This class is an EF Core interceptor that counts the queries the context runs, so a test
///     can prove how many reads a code path costs.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    /// <summary>
    ///     This property is the number of queries run so far.
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
}
