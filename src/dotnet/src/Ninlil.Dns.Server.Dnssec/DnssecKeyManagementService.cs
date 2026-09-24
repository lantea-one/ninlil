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
///     <see cref="IDnssecKeyRepository" />.
/// </summary>
/// <param name="keyRepository">The repository this service persists keys through.</param>
/// <param name="unitOfWork">The unit of work this service commits through.</param>
/// <param name="dataProtectionProvider">
///     The provider used to encrypt and decrypt a key's private key material, so the raw private
///     key never sits in the database in plain text -- the same mechanism
///     <c>Ninlil.Dns.Server.Tsig.TsigService</c> already uses for a TSIG shared secret.
/// </param>
/// <param name="algorithmPolicy">
///     The algorithm-restriction policy, consulted by <see cref="GenerateKeyAsync" />.
///     <see langword="null" /> (the default, so a caller/test with no policy configured keeps
///     compiling unchanged) or an empty <see cref="DnssecAlgorithmPolicyOptions.AllowedAlgorithms" />
///     both mean "no restriction." Read fresh (<see cref="IOptionsMonitor{TOptions}.CurrentValue" />)
///     on every <see cref="GenerateKeyAsync" /> call rather than snapshotted once -- this service
///     is registered scoped, but the plain <see cref="IOptions{TOptions}" /> a scoped registration
///     would otherwise receive is itself backed by a singleton whose <c>.Value</c> is computed
///     once and cached for the life of the process, so a configuration-file write would never be
///     observed without reading through the monitor instead.
/// </param>
public class DnssecKeyManagementService(
    IDnssecKeyRepository keyRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    IOptionsMonitor<DnssecAlgorithmPolicyOptions> algorithmPolicy = null
) : IDnssecKeyManagementService
{
    /// <summary>
    ///     This property contains the protector used to encrypt and decrypt a key's private key
    ///     material.
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
        // An operator-configured allow-list is checked first, ahead of whether this project can
        // even implement the algorithm at all -- a deliberately restricted policy (e.g. FIPS-only)
        // should reject a disallowed-but-otherwise-supported algorithm with its own specific
        // message, rather than the generic "not supported" one below.
        List<DnssecAlgorithmEnum> allowedAlgorithms = algorithmPolicy
            ?.CurrentValue
            ?.AllowedAlgorithms;

        if (allowedAlgorithms is { Count: > 0 } && !allowedAlgorithms.Contains(algorithm))
            throw new ValidationException(
                $"DNSSEC algorithm '{algorithm}' is not permitted by this server's own configured algorithm policy."
            );

        // Only after clearing the policy check does this confirm the algorithm is one this
        // project can actually generate a key for at all (excludes algorithm 12/ECC-GOST; see
        // DnssecAlgorithmRegistry's own doc comment for why).
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

        // Exactly one rollover per role, per zone, at a time -- a second one starting mid-flight
        // would leave this zone's own signing pipeline unable to tell which incoming key is
        // actually being promoted toward, or which outgoing key is actually stepping down.
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

        // GenerateKeyAsync above always creates a brand-new key already Active, since that is the
        // right state for every ordinary (non-rollover) caller -- a rollover's own incoming key
        // instead starts one step earlier, per RFC 6781 §4.1.1.1 (Pre-Publish, for a ZSK) or
        // §4.1.2 (Double-Signature, for a KSK); see DnssecKeyLifecycleStateEnum's own doc comment
        // for why a KSK's rollover starts in DoubleSignature rather than Published.
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
    ///     This method builds the DNSKEY RDATA a new key's own key tag is computed from -- the
    ///     Zone Key flag is always set (per RFC 4034 §2.1.1, every key this project generates is
    ///     a zone key), and the Secure Entry Point flag is set if, and only if, the key is a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />, the conventional (but purely advisory) signal RFC
    ///     4034 §2.1.1 describes.
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
