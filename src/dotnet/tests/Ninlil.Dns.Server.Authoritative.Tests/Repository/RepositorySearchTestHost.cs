using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class builds a real, throwaway SQLite-backed dependency injection host for
///     <see cref="ZoneRepositorySearchTests" />/<see cref="RecordRepositorySearchTests" /> to
///     resolve <c>IZoneRepository</c>/<c>IRecordRepository</c> against -- the same shape
///     <c>Ninlil.Dns.Server.QueryLog.Tests.QueryLogTestHost</c> already establishes for
///     <c>IDnsQueryLogRepository</c>'s own search/count tests, since these two tests classes are
///     proving the same kind of thing: a real <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> query
///     against a real SQLite database, not an in-memory sort/skip/take over a fully materialized
///     result.
/// </summary>
public sealed class RepositorySearchTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-repository-search-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the real dependency injection container this test's own scopes
    ///     are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds and starts the host, applying the entity model directly (via
    ///     <c>EnsureCreatedAsync</c>) rather than the real relational migrations, the same
    ///     shortcut this project's own comparable write-path service tests already take.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Host?.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return ValueTask.CompletedTask;
    }
}
