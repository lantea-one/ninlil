using System.Data.Common;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Claims;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Token;
using Xunit.Abstractions;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests <see cref="AccessTokenRevalidator" /> against a SQLite database through the
///     Entity Framework Core repositories, with a controllable clock: a disabled, locked-out or
///     missing account is rejected, the principal's role and permission claims are replaced by the
///     account's current ones, a warm cache runs no database command, <c>Invalidate</c> makes the
///     next call read again, and an entry ages out after <see cref="AccessTokenRevalidator.CacheLifetime" />.
///     It also prints the cost of a warm call, which the work notes quote.
/// </summary>
public sealed class AccessTokenRevalidatorTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-access-token-revalidator-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly ITestOutputHelper _output;
    private readonly CommandCounter _commands = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ServiceProvider _services;
    private readonly AccessTokenRevalidator _revalidator;

    public AccessTokenRevalidatorTests(ITestOutputHelper output)
    {
        _output = output;

        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        optionsBuilder.AddInterceptors(_commands);
        DbContextOptions<NinlilDnsContext> options = optionsBuilder.Options;

        using (NinlilDnsContext setup = new(options))
            setup.Database.EnsureCreated();

        // Each scope gets its own context, as a request scope does in the host.
        ServiceCollection services = new();
        services.AddScoped(_ => new NinlilDnsContext(options));
        services.AddScoped<IUserRepository>(provider => new UserRepository(
            provider.GetRequiredService<NinlilDnsContext>()
        ));
        services.AddScoped<IRoleRepository>(provider => new RoleRepository(
            provider.GetRequiredService<NinlilDnsContext>()
        ));
        services.AddScoped<IUnitOfWork>(provider => new EntityFrameworkUnitOfWork(
            provider.GetRequiredService<NinlilDnsContext>()
        ));
        services.AddScoped<IUserClaimsBuilder>(provider => new UserClaimsBuilder(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IRoleRepository>()
        ));
        _services = services.BuildServiceProvider();

        _revalidator = new AccessTokenRevalidator(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _time
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This class counts the commands the context runs, so a test can prove a warm call reads
    ///     nothing.
    /// </summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, stoppingToken);
        }
    }

    /// <summary>
    ///     This class is a clock a test advances by hand, so the cache lifetime needs no waiting.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1_000_000;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan by) => _timestamp += (long)(by.TotalSeconds * 1_000);
    }

    /// <summary>
    ///     This method runs a change against the database in a scope of its own.
    /// </summary>
    private async Task WithScopeAsync(Func<IServiceProvider, Task> change)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        await change(scope.ServiceProvider);
    }

    /// <summary>
    ///     This method adds a user holding the given direct permissions and returns its id.
    /// </summary>
    private async Task<Guid> SeedUserAsync(params string[] permissions)
    {
        Guid id = Guid.NewGuid();
        await WithScopeAsync(async provider =>
        {
            IUserRepository users = provider.GetRequiredService<IUserRepository>();
            await users.AddAsync(
                new UserEntity
                {
                    Id = id,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UserName = $"u-{id:N}",
                    NormalizedUserName = $"U-{id:N}",
                }
            );
            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await users.AddClaimsAsync(
                id,
                permissions.Select(permission => (NinlilDnsClaimTypes.Permission, permission))
            );
            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });
        return id;
    }

    private Task ChangeUserAsync(Guid id, Action<UserEntity> change) =>
        WithScopeAsync(async provider =>
        {
            IUserRepository users = provider.GetRequiredService<IUserRepository>();
            UserEntity user = await users.GetByIdAsync(id);
            change(user);
            users.Update(user);
            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

    /// <summary>
    ///     This method builds the principal a validated token gives: a subject, the permissions
    ///     the token was issued with, and the stamp claim of an account whose security stamp is
    ///     still unset, as <see cref="SeedUserAsync" /> creates it.
    /// </summary>
    private static ClaimsPrincipal TokenFor(Guid id, params string[] permissions) =>
        TokenWithStamp(id, SecurityStampClaim.Hash(null), permissions);

    /// <summary>
    ///     This method builds the principal of a token that carries the given stamp claim value,
    ///     or no stamp claim when the value is <see langword="null" />.
    /// </summary>
    private static ClaimsPrincipal TokenWithStamp(
        Guid id,
        string stampClaim,
        params string[] permissions
    ) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, id.ToString()),
                    .. permissions.Select(permission => new Claim(
                        NinlilDnsClaimTypes.Permission,
                        permission
                    )),
                    .. stampClaim is null
                        ? Array.Empty<Claim>()
                        : [new Claim(NinlilDnsClaimTypes.SecurityStamp, stampClaim)],
                ],
                "Bearer"
            )
        );

    private static string[] PermissionsOf(ClaimsPrincipal principal) =>
        [
            .. principal
                .FindAll(NinlilDnsClaimTypes.Permission)
                .Select(claim => claim.Value)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    [Fact]
    public async Task RevalidateAsync_AnEnabledAccount_IsAcceptedAndKeepsItsOtherClaims()
    {
        Guid id = await SeedUserAsync("zones:read");
        ClaimsPrincipal principal = TokenFor(id, "zones:read");

        Assert.True(await _revalidator.RevalidateAsync(principal));

        Assert.Equal(["zones:read"], PermissionsOf(principal));
        Assert.Equal(id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    }

    [Fact]
    public async Task RevalidateAsync_ATokenIssuedBeforeTheStampChanged_IsRejected()
    {
        Guid id = await SeedUserAsync("zones:read");
        ClaimsPrincipal issuedBefore = TokenFor(id, "zones:read");
        Assert.True(await _revalidator.RevalidateAsync(issuedBefore));

        await ChangeUserAsync(id, user => user.SecurityStamp = Guid.NewGuid().ToString("N"));
        _revalidator.Invalidate();

        Assert.False(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
    }

    [Fact]
    public async Task RevalidateAsync_ATokenIssuedAfterTheStampChanged_IsAccepted()
    {
        Guid id = await SeedUserAsync("zones:read");
        string stamp = Guid.NewGuid().ToString("N");
        await ChangeUserAsync(id, user => user.SecurityStamp = stamp);

        Assert.True(
            await _revalidator.RevalidateAsync(
                TokenWithStamp(id, SecurityStampClaim.Hash(stamp), "zones:read")
            )
        );
    }

    [Fact]
    public async Task RevalidateAsync_ATokenWithoutAStampClaim_IsRejected()
    {
        Guid id = await SeedUserAsync("zones:read");

        Assert.False(await _revalidator.RevalidateAsync(TokenWithStamp(id, null, "zones:read")));
    }

    [Fact]
    public async Task RevalidateAsync_ATokenIssuedByTheTokenService_IsAcceptedForAnAccountWithAStamp()
    {
        Guid id = await SeedUserAsync("zones:read");
        string stamp = Guid.NewGuid().ToString("N");
        await ChangeUserAsync(id, user => user.SecurityStamp = stamp);

        // Issuing a token only reads the claims builder; the session repository and the unit of
        // work belong to refresh tokens.
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        UserEntity user = await scope
            .ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByIdAsync(id);
        JwtTokenService tokenService = new(
            Microsoft.Extensions.Options.Options.Create(
                new JwtOptions { SigningKey = "a-signing-key-of-at-least-32-bytes!!" }
            ),
            scope.ServiceProvider.GetRequiredService<IUserClaimsBuilder>(),
            null,
            null
        );

        AccessTokenResult issued = await tokenService.CreateAccessTokenAsync(user);
        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);

        Assert.Equal(
            SecurityStampClaim.Hash(stamp),
            parsed.Claims.Single(claim => claim.Type == NinlilDnsClaimTypes.SecurityStamp).Value
        );
        Assert.True(
            await _revalidator.RevalidateAsync(
                new ClaimsPrincipal(new ClaimsIdentity(parsed.Claims, "Bearer"))
            )
        );
    }

    [Fact]
    public void SecurityStampClaim_Hash_IsADigestThatHidesTheStamp()
    {
        const string stamp = "VGVOZWVYRUNKT0JMWUVPSEdQUURVTE1DTA";

        string hash = SecurityStampClaim.Hash(stamp);

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(stamp, hash, StringComparison.Ordinal);
        Assert.Equal(hash, SecurityStampClaim.Hash(stamp));
        Assert.NotEqual(hash, SecurityStampClaim.Hash(stamp + "x"));
        Assert.Equal(SecurityStampClaim.Hash(""), SecurityStampClaim.Hash(null));
    }

    [Fact]
    public async Task RevalidateAsync_ADisabledAccount_IsRejected()
    {
        Guid id = await SeedUserAsync("zones:read");
        await ChangeUserAsync(id, user => user.IsDisabled = true);

        Assert.False(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
    }

    [Fact]
    public async Task RevalidateAsync_ALockedOutAccount_IsRejectedUntilTheLockoutEnds()
    {
        Guid id = await SeedUserAsync("zones:read");
        await ChangeUserAsync(
            id,
            user =>
            {
                user.LockoutEnabled = true;
                user.LockoutEndUtc = DateTimeOffset.UtcNow.AddHours(1);
            }
        );
        Assert.False(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        await ChangeUserAsync(id, user => user.LockoutEndUtc = null);
        _revalidator.Invalidate();

        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
    }

    [Fact]
    public async Task RevalidateAsync_AMissingAccountOrSubject_IsRejected()
    {
        Assert.False(await _revalidator.RevalidateAsync(TokenFor(Guid.NewGuid())));
        Assert.False(
            await _revalidator.RevalidateAsync(new ClaimsPrincipal(new ClaimsIdentity("Bearer")))
        );
        Assert.False(
            await _revalidator.RevalidateAsync(
                new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid")],
                        "Bearer"
                    )
                )
            )
        );
    }

    [Fact]
    public async Task RevalidateAsync_ReplacesTheTokensPermissionsWithTheAccountsCurrentOnes()
    {
        Guid id = await SeedUserAsync("users:read", "zones:write");
        ClaimsPrincipal principal = TokenFor(id, "zones:read", "zones:write");

        Assert.True(await _revalidator.RevalidateAsync(principal));

        // zones:read was revoked and users:read granted after the token was issued.
        Assert.Equal(["users:read", "zones:write"], PermissionsOf(principal));
    }

    [Fact]
    public async Task RevalidateAsync_AWarmCache_RunsNoDatabaseCommand()
    {
        Guid id = await SeedUserAsync("zones:read");
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
        int afterFirst = _commands.Count;
        Assert.True(afterFirst > 0);

        for (int i = 0; i < 1_000; i++)
            Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        Assert.Equal(afterFirst, _commands.Count);
    }

    [Fact]
    public async Task RevalidateAsync_CostOfAWarmCall_IsMeasured()
    {
        Guid id = await SeedUserAsync(
            "zones:read",
            "zones:write",
            "users:read",
            "dns64settings:read"
        );
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        const int calls = 200_000;
        ClaimsPrincipal[] principals =
        [
            .. Enumerable
                .Range(0, calls)
                .Select(_ => TokenFor(id, "zones:read", "zones:write", "users:read")),
        ];

        long before = Stopwatch.GetTimestamp();
        foreach (ClaimsPrincipal principal in principals)
            await _revalidator.RevalidateAsync(principal);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(before);

        _output.WriteLine(
            $"Warm RevalidateAsync: {elapsed.TotalMilliseconds * 1000 / calls:F2} microseconds per call over {calls} calls, 0 database commands."
        );
        Assert.True(elapsed.TotalMilliseconds * 1000 / calls < 1000);
    }

    [Fact]
    public async Task Invalidate_MakesTheNextCallReadTheDatabaseAgain()
    {
        Guid id = await SeedUserAsync("zones:read");
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        await ChangeUserAsync(id, user => user.IsDisabled = true);

        // Still the cached answer: nobody told the cache.
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        _revalidator.Invalidate();

        Assert.False(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
    }

    [Fact]
    public async Task RevalidateAsync_AfterTheCacheLifetime_ReadsTheDatabaseAgain()
    {
        Guid id = await SeedUserAsync("zones:read");
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
        await ChangeUserAsync(id, user => user.IsDisabled = true);

        _time.Advance(AccessTokenRevalidator.CacheLifetime - TimeSpan.FromSeconds(1));
        Assert.True(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await _revalidator.RevalidateAsync(TokenFor(id, "zones:read")));
    }
}
