using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the controller half of the scope rename race. It builds
///     <see cref="DhcpScopesController" /> by hand over EF Core repositories on a temporary
///     SQLite file, without a web host. A lease repository decorator inserts a lease under the
///     old scope name through a second context right after the controller's first read of the old
///     name, the way the DHCP engine does while a rename is in flight. The controller's second
///     pass after its save must move that lease too.
/// </summary>
public sealed class DhcpScopeRenameRaceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-web-scope-rename-race-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly DbContextOptions<NinlilDnsContext> _options;

    public DhcpScopeRenameRaceTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        _options = optionsBuilder.Options;

        using NinlilDnsContext context = new(_options);
        context.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task Patch_WhenTheEngineWritesALeaseUnderTheOldNameDuringTheRename_MovesItToo()
    {
        Guid scopeId = await SeedScopeWithLeaseAsync("race-old", "race-client-1");

        await using NinlilDnsContext context = new(_options);
        DhcpScopesController controller = CreateController(context, "race-old", "race-client-2");

        IActionResult result = await controller.Patch(
            scopeId,
            JsonSerializer.SerializeToElement(new { name = "race-new" }),
            default
        );

        Assert.IsType<NoContentResult>(result);
        await using NinlilDnsContext verify = new(_options);
        List<string> names = await verify
            .Set<LeaseEntity>()
            .Select(lease => lease.ScopeName)
            .ToListAsync();
        Assert.Equal(2, names.Count);
        Assert.All(names, name => Assert.Equal("race-new", name));
    }

    [Fact]
    public async Task Replace_WhenTheEngineWritesALeaseUnderTheOldNameDuringTheRename_MovesItToo()
    {
        Guid scopeId = await SeedScopeWithLeaseAsync("race-put-old", "race-client-1");

        await using NinlilDnsContext context = new(_options);
        DhcpScopesController controller = CreateController(
            context,
            "race-put-old",
            "race-client-2"
        );

        IActionResult result = await controller.Replace(
            scopeId,
            new DhcpScopeWriteRequest(
                "race-put-new",
                InternetProtocolVersionEnum.IPv4,
                "192.0.2.10",
                "192.0.2.20",
                24,
                TimeSpan.FromHours(1),
                TimeSpan.FromDays(1)
            ),
            default
        );

        Assert.IsType<NoContentResult>(result);
        await using NinlilDnsContext verify = new(_options);
        List<string> names = await verify
            .Set<LeaseEntity>()
            .Select(lease => lease.ScopeName)
            .ToListAsync();
        Assert.Equal(2, names.Count);
        Assert.All(names, name => Assert.Equal("race-put-new", name));
    }

    private DhcpScopesController CreateController(
        NinlilDnsContext context,
        string oldName,
        string racingClient
    )
    {
        LeaseRepository real = new(context);
        ILeaseRepository proxy = DispatchProxy.Create<ILeaseRepository, DelegateProxy>();
        bool done = false;
        ((DelegateProxy)(object)proxy).Handler = (method, args) =>
        {
            object result;
            try
            {
                result = method.Invoke(real, args);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }

            if (
                done
                || method.Name != nameof(ILeaseRepository.ListByScopeNameAsync)
                || !string.Equals((string)args[0], oldName, StringComparison.Ordinal)
            )
                return result;

            done = true;
            return InsertAfterAsync(
                (Task<IReadOnlyList<LeaseEntity>>)result,
                oldName,
                racingClient
            );
        };

        return new DhcpScopesController(
            new DhcpScopeRepository(context),
            new DhcpReservationRepository(context),
            new DhcpScopeCustomOptionRepository(context),
            proxy,
            new ZoneRepository(context),
            new EntityFrameworkUnitOfWork(context),
            statisticsService: null
        );
    }

    /// <summary>
    ///     This method lets the controller's read finish, then commits a lease under the old name
    ///     through a second context, as the engine does, and returns the (now stale) read.
    /// </summary>
    private async Task<IReadOnlyList<LeaseEntity>> InsertAfterAsync(
        Task<IReadOnlyList<LeaseEntity>> read,
        string scopeName,
        string clientIdentifier
    )
    {
        IReadOnlyList<LeaseEntity> stale = await read;

        await using NinlilDnsContext other = new(_options);
        other.Set<LeaseEntity>().Add(NewLease(scopeName, clientIdentifier, "192.0.2.12"));
        await other.SaveChangesAsync();
        return stale;
    }

    private async Task<Guid> SeedScopeWithLeaseAsync(string name, string clientIdentifier)
    {
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = "192.0.2.10",
            RangeEndAddress = "192.0.2.20",
            SubnetPrefixLength = 24,
            DefaultLeaseDuration = TimeSpan.FromHours(1),
            MaxLeaseDuration = TimeSpan.FromDays(1),
            GatewayAddresses = [],
            DnsServerAddresses = [],
        };

        await using NinlilDnsContext context = new(_options);
        context.Set<DhcpScopeEntity>().Add(scope);
        context.Set<LeaseEntity>().Add(NewLease(name, clientIdentifier, "192.0.2.11"));
        await context.SaveChangesAsync();
        return scope.Id;
    }

    private static LeaseEntity NewLease(
        string scopeName,
        string clientIdentifier,
        string address
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scopeName,
            ClientIdentifier = clientIdentifier,
            IpAddress = address,
            State = LeaseStateEnum.Active,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        };

    /// <summary>This class is a dispatch proxy that hands every call to <see cref="Handler" />.</summary>
    public class DelegateProxy : DispatchProxy
    {
        /// <summary>This property is the function that answers each call.</summary>
        public Func<MethodInfo, object[], object> Handler { get; set; }

        /// <inheritdoc />
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            Handler(targetMethod, args);
    }
}
