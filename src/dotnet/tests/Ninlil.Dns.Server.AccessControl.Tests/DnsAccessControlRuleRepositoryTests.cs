using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Tests;

/// <summary>
///     This class proves <see cref="DnsAccessControlRuleRepository" /> actually round-trips a
///     <see cref="DnsAccessControlRuleEntity" /> against a real SQLite database -- not just "the
///     code compiles against <see cref="EntityFrameworkRepositoryBase{TEntity}" />'s own generic
///     shape."
/// </summary>
public sealed class DnsAccessControlRuleRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-access-control-rule-repository-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;

    public DnsAccessControlRuleRepositoryTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task AddAsyncThenSaveChangesPersistsARuleThatGetByIdAsyncCanReadBack()
    {
        DnsAccessControlRuleRepository repository = new(_context);
        Guid ruleId = Guid.NewGuid();

        await repository.AddAsync(
            new DnsAccessControlRuleEntity
            {
                Id = ruleId,
                Name = "Trusted LAN",
                Cidr = "203.0.113.0/24",
                Capabilities =
                    DnsAccessControlCapabilityEnum.RecursionAccess
                    | DnsAccessControlCapabilityEnum.RateLimitBypass,
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _context.SaveChangesAsync();

        // A fresh context, standing in for a fresh scope/request, confirms this was actually
        // written to the database rather than merely still tracked in memory by the same context
        // instance that wrote it.
        DbContextOptionsBuilder<NinlilDnsContext> freshOptionsBuilder = new();
        freshOptionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        await using NinlilDnsContext freshContext = new(freshOptionsBuilder.Options);
        DnsAccessControlRuleRepository freshRepository = new(freshContext);

        DnsAccessControlRuleEntity roundTripped = await freshRepository.GetByIdAsync(ruleId);

        Assert.NotNull(roundTripped);
        Assert.Equal("Trusted LAN", roundTripped.Name);
        Assert.Equal("203.0.113.0/24", roundTripped.Cidr);
        Assert.Equal(
            DnsAccessControlCapabilityEnum.RecursionAccess
                | DnsAccessControlCapabilityEnum.RateLimitBypass,
            roundTripped.Capabilities
        );
        Assert.False(roundTripped.IsDisabled);
    }

    [Fact]
    public async Task ListEnabledAsyncExcludesDisabledRulesOnly()
    {
        DnsAccessControlRuleRepository repository = new(_context);

        await repository.AddAsync(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Enabled rule",
                Cidr = "203.0.113.0/24",
                Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await repository.AddAsync(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Disabled rule",
                Cidr = "198.51.100.0/24",
                Capabilities = DnsAccessControlCapabilityEnum.RateLimitBypass,
                IsDisabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _context.SaveChangesAsync();

        IReadOnlyList<DnsAccessControlRuleEntity> enabledRules =
            await repository.ListEnabledAsync();

        DnsAccessControlRuleEntity onlyRule = Assert.Single(enabledRules);
        Assert.Equal("Enabled rule", onlyRule.Name);
    }

    [Fact]
    public async Task UpdateThenRemoveBothPersistAcrossASaveChanges()
    {
        DnsAccessControlRuleRepository repository = new(_context);
        Guid ruleId = Guid.NewGuid();

        DnsAccessControlRuleEntity rule = new()
        {
            Id = ruleId,
            Name = "Original name",
            Cidr = "203.0.113.0/24",
            Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(rule);
        await _context.SaveChangesAsync();

        rule.Name = "Renamed";
        rule.IsDisabled = true;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.Update(rule);
        await _context.SaveChangesAsync();

        DnsAccessControlRuleEntity updated = await repository.GetByIdAsync(ruleId);
        Assert.Equal("Renamed", updated.Name);
        Assert.True(updated.IsDisabled);

        repository.Remove(updated);
        await _context.SaveChangesAsync();

        Assert.Null(await repository.GetByIdAsync(ruleId));
    }
}
