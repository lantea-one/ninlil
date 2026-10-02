import { ZoneTypeEnum } from '../../api-client';

/**
 * This constant lists every {@link ZoneTypeEnum} value in display order for the zone-type select
 * in `ZoneFormComponent`: primary and secondary roles first, then forwarding, catalog and cloud
 * types. The order is chosen for the operator, not alphabetical.
 */
export const ZONE_TYPE_OPTIONS: readonly ZoneTypeEnum[] = [
  ZoneTypeEnum.Primary,
  ZoneTypeEnum.Secondary,
  ZoneTypeEnum.Stub,
  ZoneTypeEnum.Forwarder,
  ZoneTypeEnum.SecondaryForwarder,
  ZoneTypeEnum.Catalog,
  ZoneTypeEnum.SecondaryCatalog,
  ZoneTypeEnum.CloudPrimary,
  ZoneTypeEnum.CloudSecondary,
];

/**
 * This function reports whether a zone type pulls its content from another server, so the zone
 * form asks for primary server addresses: a secondary, a stub (which pulls only the delegation
 * records), a secondary forwarder or a secondary catalog.
 */
export function requiresPrimaryServerAddresses(type: ZoneTypeEnum): boolean {
  return (
    type === ZoneTypeEnum.Secondary ||
    type === ZoneTypeEnum.Stub ||
    type === ZoneTypeEnum.SecondaryForwarder ||
    type === ZoneTypeEnum.SecondaryCatalog
  );
}

/** This function reports whether a zone type forwards every query it receives to configured upstream addresses. */
export function requiresForwarderAddresses(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Forwarder || type === ZoneTypeEnum.SecondaryForwarder;
}

/**
 * This function reports whether a zone type syncs with a cloud DNS provider, so the zone form asks
 * for a cloud provider credential and the provider's zone id.
 */
export function isCloudZoneType(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.CloudPrimary || type === ZoneTypeEnum.CloudSecondary;
}

/**
 * This function reports whether this server originates a zone type's content rather than
 * transferring or forwarding it. Only these types (`Primary` and `CloudPrimary`) are signed locally
 * and get CAA and PTR auto-management, matching `CaaRecordReconciliationService.IsEligibleZoneType`
 * and `PtrSyncService.IsEligibleZoneType`. RFC 2136 dynamic updates are narrower: the backend
 * accepts them for `Primary` zones only.
 */
export function isLocallyOriginatedZoneType(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Primary || type === ZoneTypeEnum.CloudPrimary;
}
