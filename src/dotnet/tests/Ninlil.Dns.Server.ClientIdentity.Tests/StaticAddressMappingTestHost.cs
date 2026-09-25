using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class builds a host with the Entity Framework storage layer on a temporary SQLite
///     database file and the <see cref="StaticAddressMappingClientIdentityResolver" /> under test,
///     like <c>Ninlil.Dns.Server.QueryLog.Tests.QueryLogTestHost</c>. It seeds and reads rows
///     through the repositories, each in a new scope.
/// </summary>
public sealed class StaticAddressMappingTestHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-static-address-mapping-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This property contains the host whose services the tests create scopes from.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This property resolves the singleton resolver under test.
    /// </summary>
    public StaticAddressMappingClientIdentityResolver Resolver =>
        Host.Services.GetRequiredService<StaticAddressMappingClientIdentityResolver>();

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
        builder.Services.AddSingleton<StaticAddressMappingClientIdentityResolver>();

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method seeds a static address mapping row through the repository, in a new scope.
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
    ///     This method seeds a known-client registry row through the repository, in a new scope,
    ///     for the test that a resolved identity finds its <see cref="DnsClientEntity" />.
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
    ///     This method looks a known client up by its <c>(ClientIdentifier, ProtocolVersion)</c>
    ///     pair, in a new scope.
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
