using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Dnssec.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests how <see cref="DnssecKeyManagementService.GenerateKeyAsync" /> applies
///     <see cref="DnssecAlgorithmPolicyOptions" />, which is how the <c>FIPS</c> preset limits the
///     algorithms of new keys. It uses an in-memory key repository, a no-op unit of work and a
///     fixed options monitor.
/// </summary>
public sealed class DnssecKeyManagementServiceAlgorithmPolicyTests
{
    private sealed class InMemoryDnssecKeyRepository : IDnssecKeyRepository
    {
        public readonly List<DnssecKeyEntity> Items = [];

        public Task<DnssecKeyEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<DnssecKeyEntity>> ListAsync(
            System.Linq.Expressions.Expression<Func<DnssecKeyEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>(
                predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
            );

        public Task AddAsync(DnssecKeyEntity entity, CancellationToken stoppingToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(DnssecKeyEntity entity) { }

        public void Remove(DnssecKeyEntity entity) => Items.Remove(entity);

        public Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
                .. Items.Where(item => item.ZoneId == zoneId),
            ]);

        public Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
            Guid zoneId,
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
                .. Items
                    .Where(item => item.ZoneId == zoneId)
                    .OrderBy(item => item.Role)
                    .ThenBy(item => item.CreatedAtUtc)
                    .Skip(skip)
                    .Take(take),
            ]);

        public Task<int> CountByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(Items.Count(item => item.ZoneId == zoneId));
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
            Task.FromResult(0);
    }

    private static DnssecKeyManagementService CreateService(
        params DnssecAlgorithmEnum[] allowedAlgorithms
    ) =>
        new(
            new InMemoryDnssecKeyRepository(),
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider(),
            new StaticOptionsMonitor<DnssecAlgorithmPolicyOptions>(
                new DnssecAlgorithmPolicyOptions { AllowedAlgorithms = [.. allowedAlgorithms] }
            )
        );

    [Fact]
    public async Task NoPolicyAtAllAllowsEveryRegisteredAlgorithm()
    {
        DnssecKeyManagementService service = new(
            new InMemoryDnssecKeyRepository(),
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider()
        );

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        Assert.Equal(DnssecAlgorithmEnum.Ed25519, key.Algorithm);
    }

    [Fact]
    public async Task AnEmptyAllowedAlgorithmsListMeansNoRestriction()
    {
        DnssecKeyManagementService service = CreateService();

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        Assert.Equal(DnssecAlgorithmEnum.Ed25519, key.Algorithm);
    }

    [Fact]
    public async Task AnAllowedAlgorithmGeneratesSuccessfully()
    {
        DnssecKeyManagementService service = CreateService(
            DnssecAlgorithmEnum.RsaSha256,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            DnssecAlgorithmEnum.EcdsaP384Sha384
        );

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            keySizeBits: 256
        );

        Assert.Equal(DnssecAlgorithmEnum.EcdsaP256Sha256, key.Algorithm);
    }

    [Fact]
    public async Task ADisallowedAlgorithmIsRejectedBeforeAnyKeyMaterialIsGenerated()
    {
        InMemoryDnssecKeyRepository repository = new();
        DnssecKeyManagementService service = new(
            repository,
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider(),
            new StaticOptionsMonitor<DnssecAlgorithmPolicyOptions>(
                new DnssecAlgorithmPolicyOptions
                {
                    AllowedAlgorithms =
                    [
                        DnssecAlgorithmEnum.RsaSha256,
                        DnssecAlgorithmEnum.EcdsaP256Sha256,
                        DnssecAlgorithmEnum.EcdsaP384Sha384,
                    ],
                }
            )
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            )
        );

        Assert.Empty(repository.Items);
    }
}
