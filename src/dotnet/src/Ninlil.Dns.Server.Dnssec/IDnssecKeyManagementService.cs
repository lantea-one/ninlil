using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This interface owns the lifecycle of a zone's DNSSEC signing keys -- generation and
///     retirement, both by explicit operator action only; there is no automated RFC 6781
///     rollover scheduler. The zone signing pipeline consumes what this service creates; it does
///     not create keys on its own.
/// </summary>
public interface IDnssecKeyManagementService
{
    /// <summary>
    ///     This method generates a brand-new key for a zone and persists it, already active.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the key belongs to.</param>
    /// <param name="role">Which role the key plays.</param>
    /// <param name="algorithm">The key's signing algorithm.</param>
    /// <param name="keySizeBits">
    ///     The key size to generate, in bits -- passed straight through to
    ///     <see cref="Algorithms.IDnssecSigningAlgorithm.GenerateKeyPair" />, so it is meaningful
    ///     only for the RSA family.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created key.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="algorithm" /> either has no registered
    ///     <see cref="Algorithms.IDnssecSigningAlgorithm" /> (algorithm 12/ECC-GOST, currently the
    ///     only such value) or is excluded by this server's own configured
    ///     <see cref="DnssecAlgorithmPolicyOptions" />.
    /// </exception>
    Task<DnssecKeyEntity> GenerateKeyAsync(
        Guid zoneId,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method retires an existing key, taking it out of active signing use without
    ///     deleting it.
    /// </summary>
    /// <param name="keyId">The identifier of the key to retire.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="NotFoundException">
    ///     Thrown when no key with <paramref name="keyId" /> exists.
    /// </exception>
    Task RetireKeyAsync(Guid keyId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method decrypts a key's private key material, ready to pass to
    ///     <see cref="Algorithms.IDnssecSigningAlgorithm.Sign" />.
    /// </summary>
    /// <param name="key">The key to decrypt.</param>
    /// <returns>The decrypted private key, as a PKCS#8 <c>PrivateKeyInfo</c>.</returns>
    byte[] DecryptPrivateKeyPkcs8(DnssecKeyEntity key);

    /// <summary>
    ///     This method starts a real, automated RFC 6781 key rollover: a brand-new key,
    ///     generated exactly as <see cref="GenerateKeyAsync" /> would, replacing
    ///     <paramref name="currentKeyId" /> -- Pre-Publish (RFC 6781 §4.1.1.1, starting in
    ///     <see cref="DnssecKeyLifecycleStateEnum.Published" />) for a
    ///     <see cref="DnssecKeyRoleEnum.Zsk" />, Double-Signature (RFC 6781 §4.1.2, starting in
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />) for a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />. The rest of the rollover -- promoting the new key
    ///     once it has been published long enough, stepping the old one down, and eventually
    ///     withdrawing it -- is walked automatically by
    ///     <c>Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c>; this method only
    ///     ever performs the one step a human (or an external caller) actually decides: which key
    ///     to roll, and when.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the key belongs to.</param>
    /// <param name="currentKeyId">
    ///     The identifier of the zone's own currently-active key of the role being rolled.
    /// </param>
    /// <param name="algorithm">
    ///     The new key's own signing algorithm -- <see langword="null" /> reuses
    ///     <paramref name="currentKeyId" />'s own algorithm; a different value performs an
    ///     algorithm rollover in the same pass.
    /// </param>
    /// <param name="keySizeBits">
    ///     The new key's own key size, in bits -- meaningful only for the RSA algorithm family.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created, incoming key.</returns>
    /// <exception cref="NotFoundException">
    ///     Thrown when no key with <paramref name="currentKeyId" /> exists for
    ///     <paramref name="zoneId" />.
    /// </exception>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="currentKeyId" /> does not currently hold
    ///     <see cref="DnssecKeyLifecycleStateEnum.Active" />, or for the same algorithm reasons
    ///     <see cref="GenerateKeyAsync" /> itself throws.
    /// </exception>
    /// <exception cref="ConflictException">
    ///     Thrown when this role already has a rollover in progress for this zone.
    /// </exception>
    Task<DnssecKeyEntity> StartRolloverAsync(
        Guid zoneId,
        Guid currentKeyId,
        DnssecAlgorithmEnum? algorithm = null,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method records that an operator has confirmed a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" /> rollover's own new DS record has actually been
    ///     published at a real, externally-hosted parent zone -- the one step in an externally
    ///     delegated KSK rollover this project genuinely cannot observe or automate on its own
    ///     (see <c>DnssecKeyRolloverBackgroundService</c>'s own doc comment for why). Meaningless,
    ///     and never needed, for a KSK whose own parent zone this same server also hosts and
    ///     signs -- that case generates and republishes its own child's DS record automatically,
    ///     with no operator action at all.
    /// </summary>
    /// <param name="keyId">The identifier of the key to confirm.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="NotFoundException">Thrown when no key with <paramref name="keyId" /> exists.</exception>
    /// <exception cref="ValidationException">
    ///     Thrown when the key is not a <see cref="DnssecKeyRoleEnum.Ksk" /> currently in
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />.
    /// </exception>
    Task ConfirmParentDsUpdatedAsync(Guid keyId, CancellationToken stoppingToken = default);
}
