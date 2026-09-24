using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Testcontainers.PostgreSql;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class builds a real, throwaway dependency injection host backed by a real PostgreSQL
///     server running in a Testcontainers-managed Docker container, the same shape
///     <see cref="QueryLogTestHost" /> provides for SQLite. It exists specifically so
///     provider-specific behavior -- most notably, whether domain-substring search matches
///     case-insensitively -- can be verified against PostgreSQL's own real query translation
///     rather than only against SQLite's, since the two providers' default <c>LIKE</c>
///     case-sensitivity genuinely differs (see
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" />'s
///     own domain-substring matching for why that distinction matters). A test class using this
///     host requires a working Docker daemon; there is no in-process fallback, since PostgreSQL
///     has no embedded/serverless mode the way SQLite does.
/// </summary>
public sealed class QueryLogPostgreSqlTestHost : IAsyncDisposable
{
    // Published on a random free host port, never 5432 itself: the machine running the tests may
    // well have its own PostgreSQL server listening there.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithPortBinding(PostgreSqlBuilder.PostgreSqlPort, assignRandomHostPort: true)
        .Build();

    /// <summary>
    ///     This property contains the real dependency injection container this test's own
    ///     scopes are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method starts the real PostgreSQL container, then builds and starts the host
    ///     against it, applying the entity model directly (via <c>EnsureCreatedAsync</c>) rather
    ///     than the real relational migrations, the same shortcut <see cref="QueryLogTestHost" />
    ///     itself already takes for SQLite.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:Engine"] = "PostgreSql";
        builder.Configuration["Storage:ConnectionString"] = _container.GetConnectionString();
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Host?.Dispose();
        await _container.DisposeAsync();
    }
}
