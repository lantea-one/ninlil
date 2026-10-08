using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Dnssec.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests that <see cref="DnssecKeyManagementService" /> refuses to generate keys
///     for the algorithms <see cref="DnssecAlgorithmRegistry.IsSigningProhibited" /> names, under
///     an empty policy, under a policy that lists them and through a rollover. It uses an
///     in-memory key repository and a no-op unit of work.
/// </summary>
public sealed class DnssecKeyManagementServiceProhibitedAlgorithmTests
{
    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
            Task.FromResult(0);
    }

    public static TheoryData<DnssecAlgorithmEnum> ProhibitedAlgorithms =>
        new()
        {
            DnssecAlgorithmEnum.RsaMd5,
            DnssecAlgorithmEnum.Dsa,
            DnssecAlgorithmEnum.RsaSha1,
            DnssecAlgorithmEnum.DsaNsec3Sha1,
            DnssecAlgorithmEnum.RsaSha1Nsec3Sha1,
        };

    [Theory]
    [MemberData(nameof(ProhibitedAlgorithms))]
    public async Task GenerateKeyAsync_ForAProhibitedAlgorithmWithAnEmptyPolicy_ThrowsValidationException(
        DnssecAlgorithmEnum algorithm
    )
    {
        InMemoryDnssecKeyRepository repository = new();
        DnssecKeyManagementService service = new(
            repository,
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider(),
            new StaticOptionsMonitor<DnssecAlgorithmPolicyOptions>(
                new DnssecAlgorithmPolicyOptions()
            )
        );

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateKeyAsync(Guid.NewGuid(), DnssecKeyRoleEnum.Zsk, algorithm)
        );

        Assert.Contains("prohibited", exception.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task GenerateKeyAsync_ForAProhibitedAlgorithmThePolicyLists_StillThrowsValidationException()
    {
        DnssecKeyManagementService service = new(
            new InMemoryDnssecKeyRepository(),
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider(),
            new StaticOptionsMonitor<DnssecAlgorithmPolicyOptions>(
                new DnssecAlgorithmPolicyOptions
                {
                    AllowedAlgorithms = [DnssecAlgorithmEnum.RsaSha1],
                }
            )
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Zsk,
                DnssecAlgorithmEnum.RsaSha1
            )
        );
    }

    [Fact]
    public async Task StartRolloverAsync_OfAnExistingRsaSha1KeyWithoutAnAlgorithm_ThrowsValidationException()
    {
        Guid zoneId = Guid.NewGuid();
        InMemoryDnssecKeyRepository repository = new();
        DnssecKeyEntity existing = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Role = DnssecKeyRoleEnum.Zsk,
            Algorithm = DnssecAlgorithmEnum.RsaSha1,
            LifecycleState = DnssecKeyLifecycleStateEnum.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        repository.Items.Add(existing);
        DnssecKeyManagementService service = new(
            repository,
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider()
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.StartRolloverAsync(zoneId, existing.Id)
        );

        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task StartRolloverAsync_OfAnExistingRsaSha1KeyToEcdsa_Succeeds()
    {
        Guid zoneId = Guid.NewGuid();
        InMemoryDnssecKeyRepository repository = new();
        DnssecKeyEntity existing = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Role = DnssecKeyRoleEnum.Zsk,
            Algorithm = DnssecAlgorithmEnum.RsaSha1,
            LifecycleState = DnssecKeyLifecycleStateEnum.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        repository.Items.Add(existing);
        DnssecKeyManagementService service = new(
            repository,
            new NoOpUnitOfWork(),
            new EphemeralDataProtectionProvider()
        );

        DnssecKeyEntity replacement = await service.StartRolloverAsync(
            zoneId,
            existing.Id,
            DnssecAlgorithmEnum.EcdsaP256Sha256
        );

        Assert.Equal(DnssecAlgorithmEnum.EcdsaP256Sha256, replacement.Algorithm);
    }

    [Theory]
    [MemberData(nameof(ProhibitedAlgorithms))]
    public void IsSigningProhibited_ForARegisteredWeakAlgorithm_IsTrueWhileTheRegistryStillVerifiesIt(
        DnssecAlgorithmEnum algorithm
    )
    {
        Assert.True(DnssecAlgorithmRegistry.IsSigningProhibited(algorithm));
        Assert.True(DnssecAlgorithmRegistry.TryGetAlgorithm(algorithm, out _));
    }

    [Fact]
    public void IsSigningProhibited_ForEccGostIsTrue_AndForTheCurrentAlgorithmsFalse()
    {
        Assert.True(DnssecAlgorithmRegistry.IsSigningProhibited(DnssecAlgorithmEnum.EccGost));
        Assert.All(
            new[]
            {
                DnssecAlgorithmEnum.RsaSha256,
                DnssecAlgorithmEnum.RsaSha512,
                DnssecAlgorithmEnum.EcdsaP256Sha256,
                DnssecAlgorithmEnum.EcdsaP384Sha384,
                DnssecAlgorithmEnum.Ed25519,
                DnssecAlgorithmEnum.Ed448,
            },
            algorithm => Assert.False(DnssecAlgorithmRegistry.IsSigningProhibited(algorithm))
        );
    }
}
