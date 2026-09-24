using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Tests.Repository;

/// <summary>
///     This class builds a real, throwaway SQLite-backed dependency injection host for this
///     project's own repository tests to resolve DHCP repositories against -- the same shape
///     <c>Ninlil.Dns.Server.Authoritative.Tests.Repository.RepositorySearchTestHost</c> already
///     establishes, extended with <see cref="Ninlil.Dhcp.Data.EntityFrameworkCore.Extension
///     .ServiceCollectionEntityFrameworkCoreExtension.AddNinlilDhcpEntityFrameworkCore" /> to
///     prove that the DHCP repositories genuinely work
///     once layered on top of the shared <see cref="NinlilDnsContext" /> the DNS extension method
///     registers, exactly the two-call composition order a real host is expected to use.
/// </summary>
public sealed class DhcpRepositorySearchTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-repository-search-tests-{Guid.NewGuid()}.sqlite3"
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

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return ValueTask.CompletedTask;
    }
}
