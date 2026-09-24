import {
  DnssecAlgorithmEnum,
  DnssecKeyDetail,
  DnssecKeyLifecycleStateEnum,
  DnssecKeyRoleEnum,
} from '../../../api-client';

/**
 * This function reports whether a given DNSSEC algorithm belongs to the RSA family, the only
 * family `DnssecKeyCreateRequest.KeySizeBits` is meaningful for -- confirmed directly against
 * that field's own doc comment ("meaningful only for the RSA algorithm family; ignored for every
 * other algorithm"), not assumed from the algorithm names alone. Every other algorithm this
 * project implements (DSA, ECDSA, EdDSA) has a key size fixed by the algorithm/curve itself, with
 * no separate size an operator could meaningfully choose.
 */
export function isRsaAlgorithm(algorithm: DnssecAlgorithmEnum): boolean {
  return (
    algorithm === DnssecAlgorithmEnum.Rsamd5 ||
    algorithm === DnssecAlgorithmEnum.Rsasha1 ||
    algorithm === DnssecAlgorithmEnum.Rsasha1Nsec3Sha1 ||
    algorithm === DnssecAlgorithmEnum.Rsasha256 ||
    algorithm === DnssecAlgorithmEnum.Rsasha512
  );
}

/** This constant contains a sensible default RSA key size for a freshly generated key, matching this project's own preset conventions. */
export const DEFAULT_RSA_KEY_SIZE_BITS = 2048;

/**
 * This function reports which CoreUI badge color best represents a key's own current RFC 6781
 * lifecycle state (`DnssecKeyEntity.LifecycleState`), so an operator can see rollover progress at
 * a glance from the key list alone -- confirmed directly against that enum's own doc comment for
 * what each state means, not guessed from the state names alone. `Published`/`DoubleSignature`
 * are both real, in-progress rollover states (an incoming key not yet canonical, or two keys of
 * the same role co-signing), so both get an attention-drawing color rather than a neutral one.
 */
export function lifecycleStateBadgeColor(state: DnssecKeyLifecycleStateEnum | undefined): string {
  switch (state) {
    case DnssecKeyLifecycleStateEnum.Active:
      return 'success';
    case DnssecKeyLifecycleStateEnum.Published:
      return 'info';
    case DnssecKeyLifecycleStateEnum.DoubleSignature:
      return 'warning';
    case DnssecKeyLifecycleStateEnum.Retired:
      return 'secondary';
    case DnssecKeyLifecycleStateEnum.Removed:
      return 'dark';
    default:
      return 'secondary';
  }
}

/**
 * This function reports whether a key is currently eligible to start a brand-new RFC 6781
 * rollover -- `IDnssecKeyManagementService.StartRolloverAsync`'s own doc comment confirms only a
 * zone's own currently-active key of a role can be rolled at all. This is a client-side
 * convenience only, to gray out an obviously-ineligible action before a round trip; the backend
 * itself remains the real source of truth (it also separately rejects a second concurrent
 * rollover of the same role with a `409`, a case this function does not attempt to predict since
 * doing so would need every sibling key's own state, not just this one key's).
 */
export function canStartRollover(key: DnssecKeyDetail): boolean {
  return key.lifecycleState === DnssecKeyLifecycleStateEnum.Active;
}

/**
 * This function reports whether a key is currently awaiting an operator's confirmation that its
 * own replacement's new DS record has actually been published at a real, externally-hosted
 * parent zone -- `IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync`'s own doc comment
 * confirms this is only ever meaningful for a Key-Signing Key currently mid-rollover
 * (`DoubleSignature`) that has not already been confirmed. This function cannot tell whether the
 * parent zone is actually externally hosted (this project has no such lookup at all), so it stays
 * available even for a KSK whose own parent this same server hosts and signs -- confirming there
 * is simply a harmless no-op from that KSK's own automated rollover walk's point of view.
 */
export function awaitingParentDsConfirmation(key: DnssecKeyDetail): boolean {
  return (
    key.role === DnssecKeyRoleEnum.Ksk &&
    key.lifecycleState === DnssecKeyLifecycleStateEnum.DoubleSignature &&
    !key.parentDsConfirmedAtUtc
  );
}
