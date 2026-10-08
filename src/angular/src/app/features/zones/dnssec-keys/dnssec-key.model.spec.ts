import {
  DnssecAlgorithmEnum,
  DnssecKeyDetail,
  DnssecKeyLifecycleStateEnum,
  DnssecKeyRoleEnum,
} from '../../../api-client';
import {
  awaitingParentDsConfirmation,
  canStartRollover,
  dnssecAlgorithmNumber,
  isRsaAlgorithm,
  isSigningProhibited,
  SIGNING_DNSSEC_ALGORITHMS,
  SIGNING_PROHIBITED_DNSSEC_ALGORITHMS,
  lifecycleStateBadgeColor,
} from './dnssec-key.model';

/** This helper builds a valid Active ZSK, with the given fields overridden. */
function key(overrides: Partial<DnssecKeyDetail>): DnssecKeyDetail {
  return {
    id: 'key-1',
    zoneId: 'zone-1',
    role: DnssecKeyRoleEnum.Zsk,
    algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
    keyTag: 1,
    isRetired: false,
    lifecycleState: DnssecKeyLifecycleStateEnum.Active,
    ...overrides,
  };
}

/**
 * These tests cover the DNSSEC key helpers: RSA detection, badge colors, rollover and DS
 * confirmation eligibility, and IANA algorithm numbers. Plain unit tests.
 */
describe('dnssec-key.model', () => {
  it('isRsaAlgorithm() is true for every RSA-family algorithm', () => {
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Rsamd5)).toBeTrue();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Rsasha1)).toBeTrue();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Rsasha1Nsec3Sha1)).toBeTrue();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Rsasha256)).toBeTrue();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Rsasha512)).toBeTrue();
  });

  it('isRsaAlgorithm() is false for every non-RSA algorithm family', () => {
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Dsa)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.DsaNsec3Sha1)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.EccGost)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Ecdsap256Sha256)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Ecdsap384Sha384)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Ed25519)).toBeFalse();
    expect(isRsaAlgorithm(DnssecAlgorithmEnum.Ed448)).toBeFalse();
  });

  it('lifecycleStateBadgeColor() maps every real lifecycle state to a distinct badge color', () => {
    expect(lifecycleStateBadgeColor(DnssecKeyLifecycleStateEnum.Active)).toBe('success');
    expect(lifecycleStateBadgeColor(DnssecKeyLifecycleStateEnum.Published)).toBe('info');
    expect(lifecycleStateBadgeColor(DnssecKeyLifecycleStateEnum.DoubleSignature)).toBe('warning');
    expect(lifecycleStateBadgeColor(DnssecKeyLifecycleStateEnum.Retired)).toBe('secondary');
    expect(lifecycleStateBadgeColor(DnssecKeyLifecycleStateEnum.Removed)).toBe('dark');
  });

  it('lifecycleStateBadgeColor() falls back to a neutral color for an unset state', () => {
    expect(lifecycleStateBadgeColor(undefined)).toBe('secondary');
  });

  it('canStartRollover() is true only for a key currently Active', () => {
    expect(
      canStartRollover(key({ lifecycleState: DnssecKeyLifecycleStateEnum.Active })),
    ).toBeTrue();
    expect(
      canStartRollover(key({ lifecycleState: DnssecKeyLifecycleStateEnum.Published })),
    ).toBeFalse();
    expect(
      canStartRollover(key({ lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature })),
    ).toBeFalse();
    expect(
      canStartRollover(key({ lifecycleState: DnssecKeyLifecycleStateEnum.Retired })),
    ).toBeFalse();
    expect(
      canStartRollover(key({ lifecycleState: DnssecKeyLifecycleStateEnum.Removed })),
    ).toBeFalse();
  });

  it('awaitingParentDsConfirmation() is true only for an unconfirmed KSK mid-rollover', () => {
    expect(
      awaitingParentDsConfirmation(
        key({
          role: DnssecKeyRoleEnum.Ksk,
          lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
          parentDsConfirmedAtUtc: null,
        }),
      ),
    ).toBeTrue();
  });

  it('awaitingParentDsConfirmation() is false for a ZSK, even mid-rollover', () => {
    expect(
      awaitingParentDsConfirmation(
        key({
          role: DnssecKeyRoleEnum.Zsk,
          lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
          parentDsConfirmedAtUtc: null,
        }),
      ),
    ).toBeFalse();
  });

  it('awaitingParentDsConfirmation() is false for a KSK not currently mid-rollover', () => {
    expect(
      awaitingParentDsConfirmation(
        key({
          role: DnssecKeyRoleEnum.Ksk,
          lifecycleState: DnssecKeyLifecycleStateEnum.Active,
          parentDsConfirmedAtUtc: null,
        }),
      ),
    ).toBeFalse();
  });

  it('awaitingParentDsConfirmation() is false once already confirmed', () => {
    expect(
      awaitingParentDsConfirmation(
        key({
          role: DnssecKeyRoleEnum.Ksk,
          lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
          parentDsConfirmedAtUtc: '2026-09-17T00:00:00Z',
        }),
      ),
    ).toBeFalse();
  });

  it('dnssecAlgorithmNumber() gives the IANA number a DS record carries', () => {
    expect(dnssecAlgorithmNumber(DnssecAlgorithmEnum.Rsasha256)).toBe(8);
    expect(dnssecAlgorithmNumber(DnssecAlgorithmEnum.Ecdsap256Sha256)).toBe(13);
    expect(dnssecAlgorithmNumber(DnssecAlgorithmEnum.Ed448)).toBe(16);
    expect(dnssecAlgorithmNumber(undefined)).toBeNull();
  });
});

describe('signing-prohibited DNSSEC algorithms', () => {
  it('lists exactly the six algorithms the server never signs with', () => {
    expect([...SIGNING_PROHIBITED_DNSSEC_ALGORITHMS].map((a) => dnssecAlgorithmNumber(a))).toEqual([
      1, 3, 5, 6, 7, 12,
    ]);
  });

  it('offers every other algorithm for signing', () => {
    expect(SIGNING_DNSSEC_ALGORITHMS.map((a) => dnssecAlgorithmNumber(a))).toEqual([
      8, 10, 13, 14, 15, 16,
    ]);
    expect(isSigningProhibited(DnssecAlgorithmEnum.Rsasha1)).toBeTrue();
    expect(isSigningProhibited(DnssecAlgorithmEnum.Ed25519)).toBeFalse();
  });
});
