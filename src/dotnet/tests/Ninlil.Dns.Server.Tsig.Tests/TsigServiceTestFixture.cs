using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class builds one host with a temporary SQLite database, shared by every test in
///     <see cref="TsigServiceTests" /> so the database is created once. Each test creates a
///     uniquely named <see cref="Data.Abstractions.Entity.TsigKeyEntity" />, so tests sharing the
///     database do not collide.
/// </summary>
public sealed class TsigServiceTestFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-tsig-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    /// <summary>
    ///     This property contains the root service provider tests create their scopes from.
    /// </summary>
    public IServiceProvider Services { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);

        _host = builder.Build();

        NinlilDnsContext context = _host.Services.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        Services = _host.Services;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
