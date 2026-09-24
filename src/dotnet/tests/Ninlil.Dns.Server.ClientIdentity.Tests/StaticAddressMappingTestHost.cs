using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class builds a real, throwaway SQLite-backed dependency injection host for
///     <see cref="StaticAddressMappingClientIdentityResolver" />'s own tests -- the same
///     "real repositories, real <c>SaveChangesAsync</c> commits, real reads back from storage"
///     shape <c>Ninlil.Dns.Server.QueryLog.Tests.QueryLogTestHost</c> already establishes.
/// </summary>
public sealed class StaticAddressMappingTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-static-address-mapping-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the real dependency injection container this test's own scopes
    ///     are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the real, singleton resolver under test.
    /// </summary>
    public StaticAddressMappingClientIdentityResolver Resolver =>
        Host.Services.GetRequiredService<StaticAddressMappingClientIdentityResolver>();

    /// <summary>
    ///     This method builds and starts the host, applying the entity model directly (via
    ///     <c>EnsureCreatedAsync</c>) rather than the real relational migrations, the same
    ///     shortcut every comparable test host in this project already takes.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<StaticAddressMappingClientIdentityResolver>();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method seeds a static address mapping row directly through the real repository, in
    ///     its own fresh scope.
    /// </summary>
    public async Task<DnsClientStaticAddressMappingEntity> SeedMappingAsync(
        string cidr,
        string clientIdentifier,
        Common.Enum.InternetProtocolVersionEnum protocolVersion,
        bool isDisabled = false
    )
    {
        DnsClientStaticAddressMappingEntity mapping = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Cidr = cidr,
            ClientIdentifier = clientIdentifier,
            ProtocolVersion = protocolVersion,
            IsDisabled = isDisabled,
        };

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDnsClientStaticAddressMappingRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientStaticAddressMappingRepository>();
        await repository.AddAsync(mapping);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return mapping;
    }

    /// <summary>
    ///     This method seeds a known-client registry row directly through the real repository, in
    ///     its own fresh scope -- used by the live end-to-end proof that a resolved identity
    ///     actually matches the exact <see cref="DnsClientEntity" /> key every other per-client
    ///     feature (filter-list assignment, schedules, pause, activity view) is itself keyed
    ///     against.
    /// </summary>
    public async Task<DnsClientEntity> SeedKnownClientAsync(
        string clientIdentifier,
        Common.Enum.InternetProtocolVersionEnum protocolVersion,
        string displayName
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            DisplayName = displayName,
        };

        DnsClientAddressEntity address = new()
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            CreatedAtUtc = now,
            ClientIdentifier = clientIdentifier,
            ProtocolVersion = protocolVersion,
            FirstSeenUtc = now,
        };

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository addressRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
        await repository.AddAsync(client);
        await addressRepository.AddAsync(address);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return client;
    }

    /// <summary>
    ///     This method looks a known client up by the exact <c>(ClientIdentifier, ProtocolVersion)</c>
    ///     pair every other per-client feature keys against, in its own fresh scope.
    /// </summary>
    public async Task<DnsClientEntity> FindKnownClientAsync(
        string clientIdentifier,
        Common.Enum.InternetProtocolVersionEnum protocolVersion
    )
    {
        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        return await repository.GetByClientIdentifierAsync(clientIdentifier, protocolVersion);
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
