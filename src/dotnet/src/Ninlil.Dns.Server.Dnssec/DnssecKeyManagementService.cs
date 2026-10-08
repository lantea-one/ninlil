using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class implements <see cref="IDnssecKeyManagementService" /> against
///     <see cref="IDnssecKeyRepository" />. Private keys are stored as PKCS#8, Base64-encoded and
///     encrypted with Data Protection under the purpose <c>"Ninlil.Dns.Server.Dnssec.PrivateKey"</c>.
/// </summary>
/// <param name="keyRepository">The repository this service persists keys through.</param>
/// <param name="unitOfWork">The unit of work this service commits through.</param>
/// <param name="dataProtectionProvider">
///     The provider used to encrypt and decrypt private keys, so they are not stored in plain
///     text.
/// </param>
/// <param name="algorithmPolicy">
///     The algorithm policy checked by <see cref="GenerateKeyAsync" />. <see langword="null" />
///     or an empty <see cref="DnssecAlgorithmPolicyOptions.AllowedAlgorithms" /> means no
///     restriction. It is read through <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> on
///     each call because <see cref="IOptions{TOptions}" /> caches its value for the life of the
///     process, even for a scoped service, and would miss configuration changes.
/// </param>
public class DnssecKeyManagementService(
    IDnssecKeyRepository keyRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    IOptionsMonitor<DnssecAlgorithmPolicyOptions> algorithmPolicy = null
) : IDnssecKeyManagementService
{
    /// <summary>
    ///     This field contains the protector used to encrypt and decrypt private keys.
    /// </summary>
    private readonly IDataProtector _privateKeyProtector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Dnssec.PrivateKey"
    );

    /// <inheritdoc />
    public async Task<DnssecKeyEntity> GenerateKeyAsync(
        Guid zoneId,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    )
    {
        // Prohibited algorithms come first: no policy, not even an empty one, can allow them.
        // Rollover calls this method, so it is covered too.
        if (DnssecAlgorithmRegistry.IsSigningProhibited(algorithm))
            throw new ValidationException(
                $"DNSSEC algorithm '{algorithm}' is prohibited for signing (RFC 8624 §3.1). "
                    + "Use ECDSAP256SHA256, ED25519 or another current algorithm."
            );

        // Check the policy next so a disallowed algorithm gets the policy message rather than
        // the generic "not supported" one.
        List<DnssecAlgorithmEnum> allowedAlgorithms = algorithmPolicy
            ?.CurrentValue
            ?.AllowedAlgorithms;

        if (allowedAlgorithms is { Count: > 0 } && !allowedAlgorithms.Contains(algorithm))
            throw new ValidationException(
                $"DNSSEC algorithm '{algorithm}' is not permitted by this server's own configured algorithm policy."
            );

        // Algorithm 12 (ECC-GOST) is not in the registry; see DnssecAlgorithmRegistry.
        if (
            !DnssecAlgorithmRegistry.TryGetAlgorithm(
                algorithm,
                out IDnssecSigningAlgorithm signingAlgorithm
            )
        )
            throw new ValidationException(
                $"DNSSEC algorithm '{algorithm}' is not supported for signing."
            );

        DnssecKeyPair keyPair = signingAlgorithm.GenerateKeyPair(keySizeBits);
        byte[] dnsKeyRdata = BuildDnsKeyRdata(role, algorithm, keyPair.PublicKeyWireFormat);

        DnssecKeyEntity entity = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Role = role,
            Algorithm = algorithm,
            PublicKeyWireFormat = keyPair.PublicKeyWireFormat,
            PrivateKeyPkcs8Encrypted = _privateKeyProtector.Protect(
                Convert.ToBase64String(keyPair.PrivateKeyPkcs8)
            ),
            KeyTag = DnssecKeyTag.Compute(algorithm, dnsKeyRdata),
            LifecycleState = DnssecKeyLifecycleStateEnum.Active,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await keyRepository.AddAsync(entity, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return entity;
    }

    /// <inheritdoc />
    public async Task RetireKeyAsync(Guid keyId, CancellationToken stoppingToken = default)
    {
        DnssecKeyEntity key =
            await keyRepository.GetByIdAsync(keyId, stoppingToken)
            ?? throw new NotFoundException("DnssecKey", keyId.ToString());

        key.LifecycleState = DnssecKeyLifecycleStateEnum.Removed;
        key.UpdatedAtUtc = DateTimeOffset.UtcNow;
        keyRepository.Update(key);

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <inheritdoc />
    public byte[] DecryptPrivateKeyPkcs8(DnssecKeyEntity key) =>
        Convert.FromBase64String(_privateKeyProtector.Unprotect(key.PrivateKeyPkcs8Encrypted));

    /// <inheritdoc />
    public async Task<DnssecKeyEntity> StartRolloverAsync(
        Guid zoneId,
        Guid currentKeyId,
        DnssecAlgorithmEnum? algorithm = null,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    )
    {
        DnssecKeyEntity current =
            await keyRepository.GetByIdAsync(currentKeyId, stoppingToken)
            ?? throw new NotFoundException("DnssecKey", currentKeyId.ToString());

        if (current.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", currentKeyId.ToString());

        if (current.LifecycleState != DnssecKeyLifecycleStateEnum.Active)
            throw new ValidationException(
                $"Key '{currentKeyId}' is not this zone's own currently-active {current.Role}, "
                    + "so it cannot start a rollover -- only an Active key can be rolled."
            );

        IReadOnlyList<DnssecKeyEntity> siblings = await keyRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );

        // One rollover per role per zone at a time; with two, the rollover service could not
        // tell which incoming key replaces which outgoing one.
        bool rolloverAlreadyInProgress = siblings.Any(key =>
            key.Id != current.Id
            && key.Role == current.Role
            && key.LifecycleState
                is DnssecKeyLifecycleStateEnum.Published
                    or DnssecKeyLifecycleStateEnum.DoubleSignature
        );

        if (rolloverAlreadyInProgress)
            throw new ConflictException(
                $"A {current.Role} rollover is already in progress for zone '{zoneId}'."
            );

        DnssecKeyEntity replacement = await GenerateKeyAsync(
            zoneId,
            current.Role,
            algorithm ?? current.Algorithm,
            keySizeBits,
            stoppingToken
        );

        // GenerateKeyAsync saves the key as Active. A rollover's incoming key starts earlier:
        // Published for a ZSK (RFC 6781 §4.1.1.1) or DoubleSignature for a KSK (§4.1.2); see
        // DnssecKeyLifecycleStateEnum for why a KSK skips Published. The key is briefly Active
        // between the two saves.
        replacement.LifecycleState =
            current.Role == DnssecKeyRoleEnum.Ksk
                ? DnssecKeyLifecycleStateEnum.DoubleSignature
                : DnssecKeyLifecycleStateEnum.Published;
        keyRepository.Update(replacement);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return replacement;
    }

    /// <inheritdoc />
    public async Task ConfirmParentDsUpdatedAsync(
        Guid keyId,
        CancellationToken stoppingToken = default
    )
    {
        DnssecKeyEntity key =
            await keyRepository.GetByIdAsync(keyId, stoppingToken)
            ?? throw new NotFoundException("DnssecKey", keyId.ToString());

        if (
            key.Role != DnssecKeyRoleEnum.Ksk
            || key.LifecycleState != DnssecKeyLifecycleStateEnum.DoubleSignature
        )
            throw new ValidationException(
                "Only a Key-Signing Key currently mid-rollover (DoubleSignature) can have its "
                    + "own parent DS update confirmed."
            );

        key.ParentDsConfirmedAtUtc = DateTimeOffset.UtcNow;
        key.UpdatedAtUtc = DateTimeOffset.UtcNow;
        keyRepository.Update(key);

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method builds the DNSKEY RDATA that a new key's key tag is computed from. The Zone
    ///     Key flag is always set (RFC 4034 §2.1.1). The Secure Entry Point flag is set only for a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />; RFC 4034 §2.1.1 makes it advisory.
    /// </summary>
    private static byte[] BuildDnsKeyRdata(
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        byte[] publicKeyWireFormat
    )
    {
        DnsKeyRecordData dnsKey = new(
            true,
            role == DnssecKeyRoleEnum.Ksk,
            algorithm,
            publicKeyWireFormat
        );

        return DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DnsKey, dnsKey);
    }
}
