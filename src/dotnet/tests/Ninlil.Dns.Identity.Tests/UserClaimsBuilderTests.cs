using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity.Claims;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests <see cref="UserClaimsBuilder" /> against a SQLite database through the
///     Entity Framework Core repositories: the roles and permissions of a user with several
///     roles, and that the number of database reads does not grow with the number of roles.
/// </summary>
public sealed class UserClaimsBuilderTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-user-claims-builder-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly ReaderCounter _readers = new();
    private readonly NinlilDnsContext _context;
    private readonly UserRepository _userRepository;
    private readonly RoleRepository _roleRepository;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly UserClaimsBuilder _builder;

    public UserClaimsBuilderTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        optionsBuilder.AddInterceptors(_readers);

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _userRepository = new UserRepository(_context);
        _roleRepository = new RoleRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
        _builder = new UserClaimsBuilder(_userRepository, _roleRepository);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This class counts the queries the context runs, so a test can compare how many reads
    ///     two users cost.
    /// </summary>
    private sealed class ReaderCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken stoppingToken = default
        )
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, stoppingToken);
        }
    }

    /// <summary>
    ///     This method adds a user with the given roles (each role with the given permissions)
    ///     and direct permissions.
    /// </summary>
    private async Task<UserEntity> SeedUserAsync(
        string userName,
        Dictionary<string, string[]> permissionsByRole,
        string[] directPermissions
    )
    {
        UserEntity user = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            EmailConfirmed = true,
        };
        await _userRepository.AddAsync(user);

        foreach ((string roleName, string[] permissions) in permissionsByRole)
        {
            RoleEntity role = new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Name = $"{userName}-{roleName}",
                NormalizedName = $"{userName}-{roleName}".ToUpperInvariant(),
            };
            await _roleRepository.AddAsync(role);
            await _unitOfWork.SaveChangesAsync();
            await _userRepository.AddToRoleAsync(user.Id, role.Id);

            foreach (string permission in permissions)
                await _roleRepository.AddClaimAsync(
                    role.Id,
                    NinlilDnsClaimTypes.Permission,
                    permission
                );
        }

        await _userRepository.AddClaimsAsync(
            user.Id,
            directPermissions.Select(permission => (NinlilDnsClaimTypes.Permission, permission))
        );
        await _unitOfWork.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task BuildClaimsAsync_AUserWithThreeRoles_ReturnsEveryRoleAndTheUnionOfPermissions()
    {
        UserEntity user = await SeedUserAsync(
            "alice",
            new Dictionary<string, string[]>
            {
                ["reader"] = ["zones:read", "records:read"],
                ["writer"] = ["zones:write", "zones:read"],
                ["empty"] = [],
            },
            ["users:read", "zones:read"]
        );

        IReadOnlyList<Claim> claims = await _builder.BuildClaimsAsync(user);

        Assert.Equal(
            ["alice-empty", "alice-reader", "alice-writer"],
            claims.Where(claim => claim.Type == ClaimTypes.Role).Select(c => c.Value).Order()
        );
        Assert.Equal(
            ["records:read", "users:read", "zones:read", "zones:write"],
            claims
                .Where(claim => claim.Type == NinlilDnsClaimTypes.Permission)
                .Select(claim => claim.Value)
                .Order()
        );
        Assert.Contains(
            claims,
            claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == user.Id.ToString()
        );
    }

    [Fact]
    public async Task BuildClaimsAsync_AUserWithNoRoles_ReturnsOnlyDirectPermissions()
    {
        UserEntity user = await SeedUserAsync("bob", [], ["users:read"]);

        IReadOnlyList<Claim> claims = await _builder.BuildClaimsAsync(user);

        Assert.DoesNotContain(claims, claim => claim.Type == ClaimTypes.Role);
        Assert.Equal(
            ["users:read"],
            claims
                .Where(claim => claim.Type == NinlilDnsClaimTypes.Permission)
                .Select(claim => claim.Value)
        );
    }

    [Fact]
    public async Task BuildClaimsAsync_MoreRoles_DoesNotRunMoreQueries()
    {
        UserEntity oneRole = await SeedUserAsync(
            "carol",
            new Dictionary<string, string[]> { ["a"] = ["zones:read"] },
            []
        );
        UserEntity threeRoles = await SeedUserAsync(
            "dave",
            new Dictionary<string, string[]>
            {
                ["a"] = ["zones:read"],
                ["b"] = ["zones:write"],
                ["c"] = ["users:read"],
            },
            []
        );

        _readers.Count = 0;
        await _builder.BuildClaimsAsync(oneRole);
        int queriesForOneRole = _readers.Count;

        _readers.Count = 0;
        await _builder.BuildClaimsAsync(threeRoles);

        Assert.Equal(queriesForOneRole, _readers.Count);
    }
}
