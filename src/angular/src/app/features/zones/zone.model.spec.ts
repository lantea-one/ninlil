import { ZoneTypeEnum } from '../../api-client';
import {
  isCloudZoneType,
  isLocallyOriginatedZoneType,
  requiresForwarderAddresses,
  requiresPrimaryServerAddresses,
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
});
