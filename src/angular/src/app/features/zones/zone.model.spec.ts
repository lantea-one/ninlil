import { FormControl } from '@angular/forms';
import { ZoneTypeEnum } from '../../api-client';
import {
  DEFAULT_NSEC3_ITERATIONS,
  isCloudZoneType,
  isLocallyOriginatedZoneType,
  MAX_NSEC3_ITERATIONS,
  ptrIpv6PrefixLengthValidator,
  requiresForwarderAddresses,
  requiresPrimaryServerAddresses,
  supportsDynamicUpdate,
  ZONE_TYPE_OPTIONS,
} from './zone.model';

describe('zone.model', () => {
  it('ZONE_TYPE_OPTIONS names every ZoneTypeEnum value exactly once', () => {
    expect([...ZONE_TYPE_OPTIONS].sort()).toEqual(Object.values(ZoneTypeEnum).sort());
  });

  it('requiresPrimaryServerAddresses() is true for every transfer-in zone type', () => {
    expect(requiresPrimaryServerAddresses(ZoneTypeEnum.Secondary)).toBeTrue();
    expect(requiresPrimaryServerAddresses(ZoneTypeEnum.Stub)).toBeTrue();
    expect(requiresPrimaryServerAddresses(ZoneTypeEnum.SecondaryForwarder)).toBeTrue();
    expect(requiresPrimaryServerAddresses(ZoneTypeEnum.SecondaryCatalog)).toBeTrue();
    expect(requiresPrimaryServerAddresses(ZoneTypeEnum.Primary)).toBeFalse();
  });

  it('requiresForwarderAddresses() is true only for Forwarder/SecondaryForwarder', () => {
    expect(requiresForwarderAddresses(ZoneTypeEnum.Forwarder)).toBeTrue();
    expect(requiresForwarderAddresses(ZoneTypeEnum.SecondaryForwarder)).toBeTrue();
    expect(requiresForwarderAddresses(ZoneTypeEnum.Primary)).toBeFalse();
  });

  it('isCloudZoneType() is true only for CloudPrimary/CloudSecondary', () => {
    expect(isCloudZoneType(ZoneTypeEnum.CloudPrimary)).toBeTrue();
    expect(isCloudZoneType(ZoneTypeEnum.CloudSecondary)).toBeTrue();
    expect(isCloudZoneType(ZoneTypeEnum.Secondary)).toBeFalse();
  });

  it('isLocallyOriginatedZoneType() is true only for Primary/CloudPrimary', () => {
    expect(isLocallyOriginatedZoneType(ZoneTypeEnum.Primary)).toBeTrue();
    expect(isLocallyOriginatedZoneType(ZoneTypeEnum.CloudPrimary)).toBeTrue();
    expect(isLocallyOriginatedZoneType(ZoneTypeEnum.Secondary)).toBeFalse();
    expect(isLocallyOriginatedZoneType(ZoneTypeEnum.CloudSecondary)).toBeFalse();
  });

  it('supportsDynamicUpdate() is true only for Primary and CloudPrimary', () => {
    for (const type of ZONE_TYPE_OPTIONS) {
      const expected = type === ZoneTypeEnum.Primary || type === ZoneTypeEnum.CloudPrimary;
      expect(supportsDynamicUpdate(type)).toBe(expected);
    }
  });

  it('the NSEC3 constants are 0 (the default) and 150 (the API maximum)', () => {
    expect(DEFAULT_NSEC3_ITERATIONS).toBe(0);
    expect(MAX_NSEC3_ITERATIONS).toBe(150);
  });

  it('ptrIpv6PrefixLengthValidator() passes an empty field and multiples of 4 from 16 to 124', () => {
    for (const value of [null, 16, 64, 124]) {
      expect(ptrIpv6PrefixLengthValidator(new FormControl(value))).toBeNull();
    }
  });

  it('ptrIpv6PrefixLengthValidator() fails a value that is not a multiple of 4, out of range or not an integer', () => {
    for (const value of [18, 12, 128, 64.5]) {
      expect(ptrIpv6PrefixLengthValidator(new FormControl(value))).toEqual({
        ptrIpv6PrefixLength: true,
      });
    }
  });
});
