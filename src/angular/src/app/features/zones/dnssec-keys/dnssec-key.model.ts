import { AbstractControl, ValidationErrors } from '@angular/forms';
import {
  DnssecAlgorithmEnum,
  DnssecKeyDetail,
  DnssecKeyLifecycleStateEnum,
  DnssecKeyRoleEnum,
} from '../../../api-client';

/**
 * This function reports whether an algorithm is in the RSA family, the only one that uses
 * `DnssecKeyCreateRequest.KeySizeBits`. The other algorithms' key sizes are fixed by the algorithm
 * or curve, so the key forms show a size field only for RSA.
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

/**
 * This constant maps each algorithm to its IANA number (the "DNS Security Algorithm Numbers"
 * registry) -- the number a DS or DNSKEY record carries, and what a registrar's form asks for.
 */
const DNSSEC_ALGORITHM_NUMBERS: Record<DnssecAlgorithmEnum, number> = {
  [DnssecAlgorithmEnum.Rsamd5]: 1,
  [DnssecAlgorithmEnum.Dsa]: 3,
  [DnssecAlgorithmEnum.Rsasha1]: 5,
  [DnssecAlgorithmEnum.DsaNsec3Sha1]: 6,
  [DnssecAlgorithmEnum.Rsasha1Nsec3Sha1]: 7,
  [DnssecAlgorithmEnum.Rsasha256]: 8,
  [DnssecAlgorithmEnum.Rsasha512]: 10,
  [DnssecAlgorithmEnum.EccGost]: 12,
  [DnssecAlgorithmEnum.Ecdsap256Sha256]: 13,
  [DnssecAlgorithmEnum.Ecdsap384Sha384]: 14,
  [DnssecAlgorithmEnum.Ed25519]: 15,
  [DnssecAlgorithmEnum.Ed448]: 16,
};

/** This function returns an algorithm's IANA number, or `null` when there is no algorithm. */
export function dnssecAlgorithmNumber(algorithm: DnssecAlgorithmEnum | undefined): number | null {
  return algorithm ? DNSSEC_ALGORITHM_NUMBERS[algorithm] : null;
}

/**
 * This constant lists the algorithms the server never signs with: RSAMD5, DSA, DSA-NSEC3-SHA1 and
 * ECC-GOST (RFC 8624 §3.1 says MUST NOT) and RSASHA1 and RSASHA1-NSEC3-SHA1 (that section says NOT
 * RECOMMENDED; the server bars them too).
 * It mirrors `DnssecAlgorithmRegistry.IsSigningProhibited`, and the backend answers 400 for any of
 * them. The key form, the rollover dialog, the zone form and the policy page offer only the
 * algorithms outside this list. The record forms (DNSKEY, RRSIG, DS, CERT) keep every value,
 * because they enter data and do not sign.
 */
export const SIGNING_PROHIBITED_DNSSEC_ALGORITHMS: readonly DnssecAlgorithmEnum[] = [
  DnssecAlgorithmEnum.Rsamd5,
  DnssecAlgorithmEnum.Dsa,
  DnssecAlgorithmEnum.Rsasha1,
  DnssecAlgorithmEnum.DsaNsec3Sha1,
  DnssecAlgorithmEnum.Rsasha1Nsec3Sha1,
  DnssecAlgorithmEnum.EccGost,
];

/** This function reports whether the server refuses to sign with an algorithm. */
export function isSigningProhibited(algorithm: DnssecAlgorithmEnum | undefined | null): boolean {
  return !!algorithm && SIGNING_PROHIBITED_DNSSEC_ALGORITHMS.includes(algorithm);
}

/** This constant contains the algorithms a key, zone or policy form offers: every one the server may sign with. */
export const SIGNING_DNSSEC_ALGORITHMS: DnssecAlgorithmEnum[] = Object.values(
  DnssecAlgorithmEnum,
).filter((algorithm) => !isSigningProhibited(algorithm));

/** This constant contains the RSA key size the key forms start with, the same 2048 bits the backend uses when none is given. */
export const DEFAULT_RSA_KEY_SIZE_BITS = 2048;

/** This constant is the smallest RSA key size in bits the server generates (other than 0, which means 2048). It mirrors `RsaSigningAlgorithm.MinimumKeySizeBits`. */
export const MIN_RSA_KEY_SIZE_BITS = 2048;

/** This constant is the largest RSA key size in bits the server generates (RFC 5702 §2.1, §2.2). It mirrors `RsaSigningAlgorithm.MaximumKeySizeBits`. */
export const MAX_RSA_KEY_SIZE_BITS = 4096;

/** This constant is the step an RSA key size must be a multiple of. It mirrors `RsaSigningAlgorithm.KeySizeStepBits`. */
export const RSA_KEY_SIZE_STEP_BITS = 64;

/** This constant is the hint under every RSA key size field, so the key form and the rollover dialog word the rule the same way. */
export const RSA_KEY_SIZE_HINT = `Must be a multiple of ${RSA_KEY_SIZE_STEP_BITS} between ${MIN_RSA_KEY_SIZE_BITS} and ${MAX_RSA_KEY_SIZE_BITS} bits. 0 means ${DEFAULT_RSA_KEY_SIZE_BITS}.`;

/**
 * This function is the validator for an RSA key size field in the key form and the rollover
 * dialog. It accepts 0 (the server's default of 2048) or a whole multiple of
 * {@link RSA_KEY_SIZE_STEP_BITS} from {@link MIN_RSA_KEY_SIZE_BITS} to {@link MAX_RSA_KEY_SIZE_BITS},
 * the same rule `RsaSigningAlgorithm.IsValidKeySize` applies, so the server never answers 400 for
 * a size the form let through. It returns the error `rsaKeySize` otherwise, including for an
 * empty field.
 */
export function rsaKeySizeValidator(control: AbstractControl): ValidationErrors | null {
  const size: unknown = control.value;
  const valid =
    typeof size === 'number' &&
    Number.isInteger(size) &&
    (size === 0 ||
      (size >= MIN_RSA_KEY_SIZE_BITS &&
        size <= MAX_RSA_KEY_SIZE_BITS &&
        size % RSA_KEY_SIZE_STEP_BITS === 0));
  return valid ? null : { rsaKeySize: true };
}

/**
 * This function picks the CoreUI badge color for a key's RFC 6781 lifecycle state, so the key list
 * shows rollover progress at a glance. `Published` (an incoming key not yet in use) and
 * `DoubleSignature` (two keys of one role both signing) are rollovers in progress, so they get
 * attention colors rather than a neutral one.
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
 * This function reports whether a key can start an RFC 6781 rollover: only an `Active` key can,
 * as `IDnssecKeyManagementService.StartRolloverAsync` requires. It only disables an obviously
 * ineligible action. The backend also answers 409 when a rollover of the same role is already in
 * progress, which this function does not predict since that depends on the other keys.
 */
export function canStartRollover(key: DnssecKeyDetail): boolean {
  return key.lifecycleState === DnssecKeyLifecycleStateEnum.Active;
}

/**
 * This function reports whether a key is waiting for the operator to confirm that the new DS
 * record is published at the parent zone: an unconfirmed KSK in `DoubleSignature`, the case
 * `IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync` accepts. The UI cannot tell whether
 * the parent is hosted elsewhere, so the action also shows for a KSK whose parent this server
 * signs; confirming there is harmless, since that rollover does not wait for it.
 */
export function awaitingParentDsConfirmation(key: DnssecKeyDetail): boolean {
  return (
    key.role === DnssecKeyRoleEnum.Ksk &&
    key.lifecycleState === DnssecKeyLifecycleStateEnum.DoubleSignature &&
    !key.parentDsConfirmedAtUtc
  );
}
