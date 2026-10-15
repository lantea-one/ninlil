using System.Security.Cryptography;
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
///     This class tests the RSA key size bounds of new DNSSEC keys: <see cref="DnssecKeyManagementService" />
///     rejects a size that is not 0 or a multiple of 64 between 2048 and 4096 bits with a
///     <see cref="ValidationException" />, for a generated key and for a rollover, and
///     <see cref="RsaSigningAlgorithm.GenerateKeyPair" /> enforces the same bounds itself. It uses
///     an in-memory key repository and a no-op unit of work. The sizes 2048, 3072 and 4096 really
///     generate RSA keys, so the tests that accept a size take a moment.
/// </summary>
public sealed class DnssecKeyManagementServiceKeySizeTests
{
    /// <summary>
    ///     This class is a unit of work that saves nothing; the in-memory repository already holds
    ///     every added key.
    /// </summary>
    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        /// <inheritdoc />
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
            Task.FromResult(0);
    }

    /// <summary>
    ///     This property contains sizes a new RSA key must not have: below the floor, between
    ///     steps, above the maximum and negative.
    /// </summary>
    public static TheoryData<int> RejectedSizes =>
        new() { 512, 1024, 1984, 2056, 4160, 8192, -2048 };

    /// <summary>
    ///     This method builds a service with no algorithm policy over <paramref name="repository" />.
    /// </summary>
    private static DnssecKeyManagementService CreateService(
        InMemoryDnssecKeyRepository repository
    ) => new(repository, new NoOpUnitOfWork(), new EphemeralDataProtectionProvider(), null);

    /// <summary>
    ///     This method returns the modulus length, in bits, of the RSA public key in
    ///     <paramref name="key" /> (RFC 3110 wire format).
    /// </summary>
    private static int ModulusBits(DnssecKeyEntity key)
    {
        // The exponent here is 65537, so its length fits the one-octet form: octet 0 is the
        // exponent length and the modulus follows the exponent.
        byte[] wireFormat = key.PublicKeyWireFormat;
        int modulusOctets = wireFormat.Length - 1 - wireFormat[0];

        return modulusOctets * 8;
    }

    [Theory]
    [MemberData(nameof(RejectedSizes))]
    public async Task GenerateKeyAsync_ForAnRsaSizeOutsideTheBounds_ThrowsValidationException(
        int keySizeBits
    )
    {
        InMemoryDnssecKeyRepository repository = new();
        DnssecKeyManagementService service = CreateService(repository);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Zsk,
                DnssecAlgorithmEnum.RsaSha256,
                keySizeBits
            )
        );

        Assert.Contains("multiple of 64", exception.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Items);
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    [InlineData(4096)]
    public async Task GenerateKeyAsync_ForAnRsaSizeInsideTheBounds_GeneratesAKeyOfThatSize(
        int keySizeBits
    )
    {
        DnssecKeyManagementService service = CreateService(new InMemoryDnssecKeyRepository());

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.RsaSha512,
            keySizeBits
        );

        Assert.Equal(keySizeBits, ModulusBits(key));
    }

    [Fact]
    public async Task GenerateKeyAsync_ForAnRsaSizeOfZero_GeneratesA2048BitKey()
    {
        DnssecKeyManagementService service = CreateService(new InMemoryDnssecKeyRepository());

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.RsaSha256
        );

        Assert.Equal(2048, ModulusBits(key));
    }

    [Fact]
    public async Task GenerateKeyAsync_ForEcdsaWithAnRsaOutOfRangeSize_IgnoresTheSize()
    {
        DnssecKeyManagementService service = CreateService(new InMemoryDnssecKeyRepository());

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            512
        );

        Assert.Equal(DnssecAlgorithmEnum.EcdsaP256Sha256, key.Algorithm);
    }

    [Fact]
    public async Task GenerateKeyAsync_ForEd25519WithAnRsaOutOfRangeSize_IgnoresTheSize()
    {
        DnssecKeyManagementService service = CreateService(new InMemoryDnssecKeyRepository());

        DnssecKeyEntity key = await service.GenerateKeyAsync(
            Guid.NewGuid(),
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519,
            8192
        );

        Assert.Equal(DnssecAlgorithmEnum.Ed25519, key.Algorithm);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4160)]
    public async Task StartRolloverAsync_ToAnRsaKeyOutsideTheBounds_ThrowsValidationExceptionAndAddsNoKey(
        int keySizeBits
    )
    {
        InMemoryDnssecKeyRepository repository = new();
        Guid zoneId = Guid.NewGuid();
        DnssecKeyEntity current = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Role = DnssecKeyRoleEnum.Zsk,
            Algorithm = DnssecAlgorithmEnum.Ed25519,
            LifecycleState = DnssecKeyLifecycleStateEnum.Active,
        };
        await repository.AddAsync(current);
        DnssecKeyManagementService service = CreateService(repository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.StartRolloverAsync(
                zoneId,
                current.Id,
                DnssecAlgorithmEnum.RsaSha256,
                keySizeBits
            )
        );

        Assert.Single(repository.Items);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2056)]
    [InlineData(8192)]
    public void GenerateKeyPair_ForAnRsaSizeOutsideTheBounds_ThrowsArgumentOutOfRangeException(
        int keySizeBits
    )
    {
        RsaSigningAlgorithm algorithm = new(
            DnssecAlgorithmEnum.RsaSha256,
            HashAlgorithmName.SHA256
        );

        Assert.Throws<ArgumentOutOfRangeException>(() => algorithm.GenerateKeyPair(keySizeBits));
    }

    [Fact]
    public void RsaSigningAlgorithm_Bounds_AreTheDocumentedOnes()
    {
        Assert.Equal(2048, RsaSigningAlgorithm.MinimumKeySizeBits);
        Assert.Equal(4096, RsaSigningAlgorithm.MaximumKeySizeBits);
        Assert.Equal(64, RsaSigningAlgorithm.KeySizeStepBits);
        Assert.True(RsaSigningAlgorithm.IsValidKeySize(0));
        Assert.False(RsaSigningAlgorithm.IsValidKeySize(2047));
    }
}
