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
/// <param name="zoneKeyGate">
///     The gate that serialises <see cref="StartRolloverAsync" /> per zone. Dependency injection
///     passes the singleton shared by every scope; when <see langword="null" /> the service uses a
///     gate of its own, which only serialises calls on this instance.
/// </param>
public class DnssecKeyManagementService(
    IDnssecKeyRepository keyRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    IOptionsMonitor<DnssecAlgorithmPolicyOptions> algorithmPolicy = null,
    DnssecZoneKeyGate zoneKeyGate = null
) : IDnssecKeyManagementService
{
    /// <summary>
    ///     This field contains the protector used to encrypt and decrypt private keys.
    /// </summary>
    private readonly IDataProtector _privateKeyProtector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Dnssec.PrivateKey"
    );

    /// <summary>
    ///     This field contains the gate that serialises key-state changes of one zone.
    /// </summary>
    private readonly DnssecZoneKeyGate _zoneKeyGate = zoneKeyGate ?? new DnssecZoneKeyGate();

    /// <inheritdoc />
    public Task<DnssecKeyEntity> GenerateKeyAsync(
        Guid zoneId,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    ) =>
        CreateKeyAsync(
            zoneId,
            role,
            algorithm,
            keySizeBits,
            DnssecKeyLifecycleStateEnum.Active,
            stoppingToken
        );

    /// <summary>
    ///     This method runs every check, generates the key pair and saves the new key in
    ///     <paramref name="initialState" /> with one save, so the key never exists in any other
    ///     state. A rollover passes Published or DoubleSignature; saving it as Active first would
    ///     show a second Active key to a concurrent signer or reader.
    /// </summary>
    private async Task<DnssecKeyEntity> CreateKeyAsync(
        Guid zoneId,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        int keySizeBits,
        DnssecKeyLifecycleStateEnum initialState,
        CancellationToken stoppingToken
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

        // Check the size here so the caller gets a 400 and not an ArgumentOutOfRangeException.
        // Only the RSA algorithms use the size; every other algorithm ignores it.
        if (
            signingAlgorithm is RsaSigningAlgorithm
            && !RsaSigningAlgorithm.IsValidKeySize(keySizeBits)
        )
            throw new ValidationException(
                $"RSA key size must be a multiple of {RsaSigningAlgorithm.KeySizeStepBits} between "
                    + $"{RsaSigningAlgorithm.MinimumKeySizeBits} and "
                    + $"{RsaSigningAlgorithm.MaximumKeySizeBits} bits; 0 means 2048."
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
            LifecycleState = initialState,
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
        // The in-progress check and the insert must be one step per zone. Without the gate two
        // concurrent starts both see no Published key and both insert one.
        using IDisposable zoneLease = await _zoneKeyGate.EnterAsync(zoneId, stoppingToken);

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

        // A KSK skips Published (see DnssecKeyLifecycleStateEnum); a ZSK is Pre-Published
        // (RFC 6781 §4.1.1.1). A KSK starts in DoubleSignature (§4.1.2).
        return await CreateKeyAsync(
            zoneId,
            current.Role,
            algorithm ?? current.Algorithm,
            keySizeBits,
            current.Role == DnssecKeyRoleEnum.Ksk
                ? DnssecKeyLifecycleStateEnum.DoubleSignature
                : DnssecKeyLifecycleStateEnum.Published,
            stoppingToken
        );
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
