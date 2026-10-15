using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This interface manages a zone's DNSSEC signing keys: generating, retiring and starting
///     rollovers, all on operator request through the DNSSEC key endpoints. Keys are stored
///     through <see cref="Ninlil.Dns.Data.Abstractions.Repository.IDnssecKeyRepository" /> with
///     the private key encrypted. The zone signing code in <c>Server.Authoritative</c> reads the
///     keys and decrypts them here; it never creates keys. Once a rollover is started,
///     <c>Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> moves it through the
///     remaining states.
/// </summary>
public interface IDnssecKeyManagementService
{
    /// <summary>
    ///     This method generates a new key for a zone and stores it in the
    ///     <see cref="DnssecKeyLifecycleStateEnum.Active" /> state.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the key belongs to.</param>
    /// <param name="role">Which role the key plays.</param>
    /// <param name="algorithm">The key's signing algorithm.</param>
    /// <param name="keySizeBits">
    ///     The key size in bits, passed to
    ///     <see cref="Algorithms.IDnssecSigningAlgorithm.GenerateKeyPair" />. Only RSA uses it: 0
    ///     means 2048, any other value must be a multiple of 64 between 2048 and 4096.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created key.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="algorithm" /> is excluded by
    ///     <see cref="DnssecAlgorithmPolicyOptions" />, or has no
    ///     <see cref="Algorithms.IDnssecSigningAlgorithm" /> (such as algorithm 12, ECC-GOST), or
    ///     when an RSA <paramref name="keySizeBits" /> is outside the bounds above.
    /// </exception>
    Task<DnssecKeyEntity> GenerateKeyAsync(
        Guid zoneId,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method takes a key out of use immediately by setting it to
    ///     <see cref="DnssecKeyLifecycleStateEnum.Removed" />. The record is kept, not deleted.
    ///     This skips the RFC 6781 rollover timing; use <see cref="StartRolloverAsync" /> to
    ///     replace a key safely.
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
    ///     This method starts an RFC 6781 rollover of <paramref name="currentKeyId" />. It generates
    ///     a replacement key with the checks of <see cref="GenerateKeyAsync" /> and saves it once,
    ///     directly in the first rollover state, so it never exists as a second Active key:
    ///     <see cref="DnssecKeyLifecycleStateEnum.Published" /> for a
    ///     <see cref="DnssecKeyRoleEnum.Zsk" /> (Pre-Publish, RFC 6781 §4.1.1.1) or
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> for a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" /> (Double-Signature, RFC 6781 §4.1.2).
    ///     Concurrent starts for one zone are serialised, so exactly one of them succeeds and the
    ///     others throw <see cref="ConflictException" />.
    ///     <c>Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> does the rest:
    ///     promoting the new key, stepping the old one down and finally removing it.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the key belongs to.</param>
    /// <param name="currentKeyId">
    ///     The identifier of the zone's currently active key of the role being rolled.
    /// </param>
    /// <param name="algorithm">
    ///     The new key's algorithm. <see langword="null" /> keeps the current key's algorithm; a
    ///     different value also changes the algorithm.
    /// </param>
    /// <param name="keySizeBits">
    ///     The new key's size in bits. Only RSA uses it: 0 means 2048, any other value must be a
    ///     multiple of 64 between 2048 and 4096 (otherwise <see cref="ValidationException" />).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created, incoming key.</returns>
    /// <exception cref="NotFoundException">
    ///     Thrown when no key with <paramref name="currentKeyId" /> exists for
    ///     <paramref name="zoneId" />.
    /// </exception>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="currentKeyId" /> is not
    ///     <see cref="DnssecKeyLifecycleStateEnum.Active" />, or for the algorithm reasons
    ///     <see cref="GenerateKeyAsync" /> throws.
    /// </exception>
    /// <exception cref="ConflictException">
    ///     Thrown when the zone already has a rollover in progress for this role (another key of
    ///     the role is Published or DoubleSignature).
    /// </exception>
    Task<DnssecKeyEntity> StartRolloverAsync(
        Guid zoneId,
        Guid currentKeyId,
        DnssecAlgorithmEnum? algorithm = null,
        int keySizeBits = 0,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method records that an operator has confirmed the new DS record for a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" /> rollover is published at an external parent zone.
    ///     The server cannot check that itself, so the rollover waits for this confirmation (see
    ///     <c>DnssecKeyRolloverBackgroundService</c>). It is not needed when this server also
    ///     hosts and signs the parent, because the parent's DS records are then updated
    ///     automatically.
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
