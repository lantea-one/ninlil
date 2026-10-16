import { AbstractControl, ValidationErrors } from '@angular/forms';
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
 * and `PtrSyncService.IsEligibleZoneType`. RFC 2136 dynamic updates use the same two types, see
 * {@link supportsDynamicUpdate}.
 */
export function isLocallyOriginatedZoneType(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Primary || type === ZoneTypeEnum.CloudPrimary;
}

/**
 * This function reports whether a zone type can accept RFC 2136 dynamic updates, so the zone form
 * offers the "Accept RFC 2136 dynamic updates" checkbox and the update-key list only for it. It
 * mirrors the type rule of `DynamicUpdateQueryResolver`, which accepts `Primary` and
 * `CloudPrimary` zones and refuses every other type. A stored `dynamicUpdateEnabled` flag on any
 * other type has no effect, so the form sends `false` for it.
 */
export function supportsDynamicUpdate(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Primary || type === ZoneTypeEnum.CloudPrimary;
}

/**
 * This constant is the NSEC3 iteration count a new zone starts with. RFC 9276 §3.1 recommends 0
 * additional iterations, and it matches `ZoneEntity.Nsec3Iterations` and `ZoneWriteRequest`.
 */
export const DEFAULT_NSEC3_ITERATIONS = 0;

/**
 * This constant is the highest NSEC3 iteration count the zones API accepts, matching
 * `ZonesController.MaximumNsec3Iterations`. Validators, this server's resolver included, may treat
 * a zone above it as insecure (RFC 9276 §3.2).
 */
export const MAX_NSEC3_ITERATIONS = 150;

/**
 * This validator checks the per-zone IPv6 PTR prefix length: an integer from 16 to 124 that is a
 * multiple of 4. It mirrors `ZonesController.ValidatePtrIpv6PrefixLength`. Each `ip6.arpa` label
 * is one nibble (4 bits, RFC 3596 §2.5), so a prefix that is not a multiple of 4 cannot name a
 * reverse zone. An empty field (`null`) passes, because it means "inherit the server default".
 * The error key is `ptrIpv6PrefixLength`.
 */
export function ptrIpv6PrefixLengthValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;

  if (value === null || value === undefined || value === '') {
    return null;
  }

  const isValid =
    Number.isInteger(value) &&
    Number(value) >= 16 &&
    Number(value) <= 124 &&
    Number(value) % 4 === 0;
  return isValid ? null : { ptrIpv6PrefixLength: true };
}
