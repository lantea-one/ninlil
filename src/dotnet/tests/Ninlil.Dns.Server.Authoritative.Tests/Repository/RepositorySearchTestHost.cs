using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class builds a DI host with the EF Core repositories on a temporary SQLite database,
///     for the repository paging tests in this folder. It follows
///     <c>Ninlil.Dns.Server.QueryLog.Tests.QueryLogTestHost</c>. The database is deleted on
///     dispose.
/// </summary>
public sealed class RepositorySearchTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-repository-search-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the real dependency injection container this test's scopes
    ///     are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds and starts the host, applying the entity model directly (via
    ///     <c>EnsureCreatedAsync</c>) rather than the real relational migrations, the same
    ///     shortcut this project's comparable write-path service tests already take.
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
