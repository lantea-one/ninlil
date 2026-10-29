using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class builds a host with the Entity Framework storage layer on a temporary SQLite
///     database file, shared by the SQLite-based test classes in this project. The file is
///     deleted on dispose.
/// </summary>
public sealed class QueryLogTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-querylog-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the host whose services the tests create scopes from.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds the host (it is not started) and creates the schema from the entity
    ///     model with <c>EnsureCreatedAsync</c> rather than running the migrations.
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

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return ValueTask.CompletedTask;
    }
}
