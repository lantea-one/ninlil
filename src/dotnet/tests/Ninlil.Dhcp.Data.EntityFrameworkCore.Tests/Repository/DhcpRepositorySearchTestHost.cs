using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Tests.Repository;

/// <summary>
///     This class builds a dependency injection host on a temporary SQLite database file for the
///     repository tests in this project, like
///     <c>Ninlil.Dns.Server.Authoritative.Tests.Repository.RepositorySearchTestHost</c>. It
///     registers the DNS context first and then <see cref="Ninlil.Dhcp.Data.EntityFrameworkCore.Extension
///     .ServiceCollectionEntityFrameworkCoreExtension.AddNinlilDhcpEntityFrameworkCore" />, the
///     order the application uses, and deletes the file on disposal.
/// </summary>
public sealed class DhcpRepositorySearchTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-repository-search-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the host whose services the tests create scopes from.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds the host and creates the schema from the model with
    ///     <c>EnsureCreatedAsync</c> instead of running the migrations, as other repository tests
    ///     do.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();

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
