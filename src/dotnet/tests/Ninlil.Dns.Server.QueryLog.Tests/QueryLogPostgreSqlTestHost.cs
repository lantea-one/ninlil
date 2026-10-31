using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Testcontainers.PostgreSql;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class builds a host like <see cref="QueryLogTestHost" />, but backed by PostgreSQL in
///     a Docker container managed by Testcontainers. It lets tests check provider-specific
///     behavior, such as case-insensitive domain search, against PostgreSQL's query translation:
///     <c>LIKE</c> is case-sensitive on PostgreSQL but not on SQLite (see the domain matching in
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" />). Tests
///     using it need a working Docker daemon; there is no fallback.
/// </summary>
public sealed class QueryLogPostgreSqlTestHost : IAsyncDisposable
{
    // Published on a random free host port rather than 5432, which a local PostgreSQL server may
    // already be using.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        "postgres:16-alpine@sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab"
    )
        .WithPortBinding(PostgreSqlBuilder.PostgreSqlPort, assignRandomHostPort: true)
        .Build();

    /// <summary>
    ///     This property contains the host whose services the tests create scopes from.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method starts the PostgreSQL container, builds the host against it (it is not
    ///     started) and creates the schema with <c>EnsureCreatedAsync</c>, as
    ///     <see cref="QueryLogTestHost" /> does.
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
