using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="StoredClientIdentifierNormalizer" /> against a private in-memory
///     SQLite database per test (no web host), through the real repositories: reservations, client
///     addresses and static mappings stored in another form are rewritten to the normal form,
///     colliding rows are left alone with a Warning that names them, unparseable values are left
///     alone with a Warning, and a second run changes nothing and saves nothing. Foreign keys are
///     off so the rows need no scope or client parents.
/// </summary>
public class StoredClientIdentifierNormalizerTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly NinlilDnsContext context;
    private readonly CountingUnitOfWork unitOfWork;

    public StoredClientIdentifierNormalizerTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF";
            pragma.ExecuteNonQuery();
        }

        context = new NinlilDnsContext(
            new DbContextOptionsBuilder<NinlilDnsContext>().UseSqlite(connection).Options
        );
        context.Database.EnsureCreated();
        unitOfWork = new CountingUnitOfWork(new EntityFrameworkUnitOfWork(context));
    }

    public void Dispose()
    {
        context.Dispose();
        connection.Dispose();
    }

    private DhcpReservationEntity Reservation(Guid scopeId, string identifier, string address)
    {
        DhcpReservationEntity row = new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scopeId,
            ClientIdentifier = identifier,
            IpAddress = address,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        context.Add(row);
        return row;
    }

    private DnsClientAddressEntity ClientAddress(
        string identifier,
        InternetProtocolVersionEnum version
    )
    {
        DnsClientAddressEntity row = new()
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ClientIdentifier = identifier,
            ProtocolVersion = version,
            Address = "192.0.2.1",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            FirstSeenUtc = DateTimeOffset.UtcNow,
        };
        context.Add(row);
        return row;
    }

    private DnsClientStaticAddressMappingEntity Mapping(string identifier)
    {
        DnsClientStaticAddressMappingEntity row = new()
        {
            Id = Guid.NewGuid(),
            Cidr = "192.0.2.0/24",
            ClientIdentifier = identifier,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        context.Add(row);
        return row;
    }

    private async Task<(int Changed, CapturingLogger Logger)> RunAsync()
    {
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        unitOfWork.Saves = 0;
        CapturingLogger logger = new();
        int changed = await StoredClientIdentifierNormalizer.RunAsync(
            new DhcpReservationRepository(context),
            new DnsClientAddressRepository(context),
            new DnsClientStaticAddressMappingRepository(context),
            unitOfWork,
            logger
        );
        context.ChangeTracker.Clear();

        return (changed, logger);
    }

    [Fact]
    public async Task RunAsync_WithAReservationInAnotherForm_NormalisesIdentifierAndAddress()
    {
        DhcpReservationEntity row = Reservation(
            Guid.NewGuid(),
            "AA-BB-CC-DD-EE-10",
            "2001:DB8:0::10"
        );

        (int changed, _) = await RunAsync();

        Assert.Equal(1, changed);
        DhcpReservationEntity stored = await context
            .Set<DhcpReservationEntity>()
            .SingleAsync(r => r.Id == row.Id);
        Assert.Equal("aa:bb:cc:dd:ee:10", stored.ClientIdentifier);
        Assert.Equal("2001:db8::10", stored.IpAddress);
        Assert.NotNull(stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task RunAsync_WithCollidingReservationsInOneScope_LeavesBothAndWarnsNamingThem()
    {
        Guid scope = Guid.NewGuid();
        Reservation(scope, "AA:BB:CC:DD:EE:11", "10.0.0.11");
        Reservation(scope, "aa-bb-cc-dd-ee-11", "10.0.0.12");

        (int changed, CapturingLogger logger) = await RunAsync();

        Assert.Equal(0, changed);
        Assert.Equal(0, unitOfWork.Saves);
        List<string> stored = await context
            .Set<DhcpReservationEntity>()
            .Where(r => r.ScopeId == scope)
            .Select(r => r.ClientIdentifier)
            .ToListAsync();
        Assert.Contains("AA:BB:CC:DD:EE:11", stored);
        Assert.Contains("aa-bb-cc-dd-ee-11", stored);
        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("'AA:BB:CC:DD:EE:11'", warning);
        Assert.Contains("'aa-bb-cc-dd-ee-11'", warning);
    }

    [Fact]
    public async Task RunAsync_WithTheSameIdentifierInTwoScopes_NormalisesBoth()
    {
        Reservation(Guid.NewGuid(), "AA-BB-CC-DD-EE-1F", "10.0.0.1");
        Reservation(Guid.NewGuid(), "AA-BB-CC-DD-EE-1F", "10.0.0.1");

        (int changed, CapturingLogger logger) = await RunAsync();

        Assert.Equal(2, changed);
        Assert.Empty(logger.Warnings);
        Assert.All(
            await context.Set<DhcpReservationEntity>().ToListAsync(),
            row => Assert.Equal("aa:bb:cc:dd:ee:1f", row.ClientIdentifier)
        );
    }

    [Fact]
    public async Task RunAsync_WithTheSameIdentifierForTwoProtocols_KeepsBothAndNormalisesTheFirst()
    {
        DnsClientAddressEntity v4 = ClientAddress(
            "AA-BB-CC-DD-EE-12",
            InternetProtocolVersionEnum.IPv4
        );
        DnsClientAddressEntity v6 = ClientAddress(
            "aa:bb:cc:dd:ee:12",
            InternetProtocolVersionEnum.IPv6
        );

        (int changed, CapturingLogger logger) = await RunAsync();

        Assert.Equal(1, changed);
        Assert.Empty(logger.Warnings);
        Assert.Equal(
            "aa:bb:cc:dd:ee:12",
            (
                await context.Set<DnsClientAddressEntity>().SingleAsync(r => r.Id == v4.Id)
            ).ClientIdentifier
        );
        Assert.Equal(
            "aa:bb:cc:dd:ee:12",
            (
                await context.Set<DnsClientAddressEntity>().SingleAsync(r => r.Id == v6.Id)
            ).ClientIdentifier
        );
    }

    [Fact]
    public async Task RunAsync_WithCollidingClientAddressesOfOneProtocol_LeavesThemAndWarns()
    {
        ClientAddress("AA-BB-CC-DD-EE-14", InternetProtocolVersionEnum.IPv4);
        ClientAddress("aa:bb:cc:dd:ee:14", InternetProtocolVersionEnum.IPv4);

        (int changed, CapturingLogger logger) = await RunAsync();

        Assert.Equal(0, changed);
        Assert.Equal(0, unitOfWork.Saves);
        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("'AA-BB-CC-DD-EE-14'", warning);
        Assert.Contains("'aa:bb:cc:dd:ee:14'", warning);
    }

    [Fact]
    public async Task RunAsync_WithAStaticMappingInAnotherForm_Normalises()
    {
        DnsClientStaticAddressMappingEntity row = Mapping("AABBCCDDEE13");

        (int changed, _) = await RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal(
            "aa:bb:cc:dd:ee:13",
            (
                await context
                    .Set<DnsClientStaticAddressMappingEntity>()
                    .SingleAsync(r => r.Id == row.Id)
            ).ClientIdentifier
        );
    }

    [Fact]
    public async Task RunAsync_WithAnUnparseableValue_LeavesTheRowAndWarns()
    {
        DnsClientStaticAddressMappingEntity row = Mapping("declined:10.0.0.5");
        Reservation(Guid.NewGuid(), "not-an-identifier", "10.0.0.5");

        (int changed, CapturingLogger logger) = await RunAsync();

        Assert.Equal(0, changed);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains(logger.Warnings, w => w.Contains("declined:10.0.0.5"));
        Assert.Contains(logger.Warnings, w => w.Contains("not-an-identifier"));
        Assert.Equal(
            "declined:10.0.0.5",
            (
                await context
                    .Set<DnsClientStaticAddressMappingEntity>()
                    .SingleAsync(r => r.Id == row.Id)
            ).ClientIdentifier
        );
    }

    [Fact]
    public async Task RunAsync_RunTwice_ChangesNothingTheSecondTimeAndDoesNotSave()
    {
        Reservation(Guid.NewGuid(), "AA-BB-CC-DD-EE-15", "10.0.0.15");
        ClientAddress("AA-BB-CC-DD-EE-15", InternetProtocolVersionEnum.IPv4);
        Mapping("AABBCCDDEE15");

        (int first, _) = await RunAsync();
        (int second, CapturingLogger logger) = await RunAsync();

        Assert.Equal(3, first);
        Assert.Equal(0, second);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    ///     This class counts the saves that reach the wrapped unit of work.
    /// </summary>
    private sealed class CountingUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public int Saves { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
        {
            Saves++;
            return inner.SaveChangesAsync(stoppingToken);
        }
    }

    /// <summary>
    ///     This class collects the formatted text of every Warning it is given.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
