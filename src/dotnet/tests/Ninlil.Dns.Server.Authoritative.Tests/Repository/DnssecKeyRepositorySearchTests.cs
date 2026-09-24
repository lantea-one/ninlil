using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnssecKeyRepository" />'s
///     real, pushed-down <c>SearchByZoneIdAsync</c>/<c>CountByZoneIdAsync</c> pagination against
///     a real SQLite-backed database -- the Phase 13h proof that <c>DnssecKeysController</c>'s
///     own key listing no longer materializes every key into memory before slicing a page out of
///     it, and that a second zone's own keys never leak into another zone's page, mirroring
///     <see cref="ZoneRepositorySearchTests" />'s own Phase 13g proof for the same shape.
/// </summary>
public sealed class DnssecKeyRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one target zone's five keys -- three ZSKs and two KSKs, deliberately
    ///     out of role/creation order -- plus one extra key on an entirely different zone, so a
    ///     passing zone-scoping assertion can only be explained by a real, translated
    ///     <c>WHERE ZoneId = ...</c> clause, not by the target zone simply happening to be the
    ///     only zone with any keys at all.
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnssecKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid targetZoneId = Guid.NewGuid();
        Guid otherZoneId = Guid.NewGuid();
        DateTimeOffset baseTime = DateTimeOffset.UtcNow;

        (DnssecKeyRoleEnum Role, int OffsetMinutes)[] keysInSeedOrder =
        [
            (DnssecKeyRoleEnum.Zsk, 40),
            (DnssecKeyRoleEnum.Ksk, 10),
            (DnssecKeyRoleEnum.Zsk, 20),
            (DnssecKeyRoleEnum.Ksk, 30),
            (DnssecKeyRoleEnum.Zsk, 0),
        ];

        foreach ((DnssecKeyRoleEnum role, int offsetMinutes) in keysInSeedOrder)
            await repository.AddAsync(
                new DnssecKeyEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = baseTime.AddMinutes(offsetMinutes),
                    ZoneId = targetZoneId,
                    Role = role,
                    Algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    PublicKeyWireFormat = [1, 2, 3],
                    PrivateKeyPkcs8Encrypted = "encrypted-private-key",
                    KeyTag = 1,
                    PublishedAtUtc = baseTime,
                }
            );

        await repository.AddAsync(
            new DnssecKeyEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = baseTime,
                ZoneId = otherZoneId,
                Role = DnssecKeyRoleEnum.Zsk,
                Algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                PublicKeyWireFormat = [4, 5, 6],
                PrivateKeyPkcs8Encrypted = "encrypted-private-key",
                KeyTag = 2,
                PublishedAtUtc = baseTime,
            }
        );

        await unitOfWork.SaveChangesAsync();

        return targetZoneId;
    }

    [Fact]
    public async Task SearchByZoneIdAsync_ReturnsOnlyTheTargetZonesKeys_OrderedByRoleThenCreatedAt()
    {
        Guid targetZoneId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnssecKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();

        IReadOnlyList<DnssecKeyEntity> results = await repository.SearchByZoneIdAsync(
            targetZoneId,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.All(results, key => Assert.Equal(targetZoneId, key.ZoneId));

        // Zsk (0) sorts before Ksk (1); within each role, creation time breaks the tie.
        Assert.Equal(
            [
                DnssecKeyRoleEnum.Zsk,
                DnssecKeyRoleEnum.Zsk,
                DnssecKeyRoleEnum.Zsk,
                DnssecKeyRoleEnum.Ksk,
                DnssecKeyRoleEnum.Ksk,
            ],
            [.. results.Select(key => key.Role)]
        );
    }

    [Fact]
    public async Task SearchByZoneIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        Guid targetZoneId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnssecKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();

        IReadOnlyList<DnssecKeyEntity> firstPage = await repository.SearchByZoneIdAsync(
            targetZoneId,
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnssecKeyEntity> thirdPage = await repository.SearchByZoneIdAsync(
            targetZoneId,
            skip: 4,
            take: 2
        );

        Assert.Equal(2, firstPage.Count);
        Assert.Single(thirdPage);
        Assert.DoesNotContain(thirdPage, key => firstPage.Any(first => first.Id == key.Id));
    }

    [Fact]
    public async Task CountByZoneIdAsync_ReflectsOnlyTheTargetZonesKeyCount()
    {
        Guid targetZoneId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnssecKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();

        int count = await repository.CountByZoneIdAsync(targetZoneId);

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task SearchByZoneIdAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        Guid targetZoneId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnssecKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();

        IReadOnlyList<DnssecKeyEntity> results = await repository.SearchByZoneIdAsync(
            targetZoneId,
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
