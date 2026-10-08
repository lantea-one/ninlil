/**
 * This module holds the DHCPv4 raw option limits the scope and tag rule forms check before they
 * call the API. They mirror `DhcpRawOptionRules.TryValidateDhcpv4` on the server, which has the
 * final say; checking here only saves a round trip and names the offending row.
 */
import { AbstractControl, ValidationErrors } from '@angular/forms';

/** The tags the server or relay agent sets itself: Option Overload, Message Type, Relay Agent Information. */
const SERVER_OWNED_TAGS: readonly number[] = [52, 53, 82];

/** The largest DHCPv4 option value in bytes: the length field is one byte (RFC 2132 Section 2). */
export const DHCPV4_MAX_OPTION_VALUE_BYTES = 255;

/**
 * This validator accepts a whole DHCPv4 option tag from 1 to 254 that the server does not own.
 * It reports `min` or `max` for a tag out of range (0 is Pad and 255 is End, RFC 2132 Sections
 * 3.1 and 3.2) and `reservedTag` for 52, 53 and 82. An empty value is left to `Validators.required`.
 */
export function dhcpv4OptionTagValidator(control: AbstractControl): ValidationErrors | null {
  const tag = control.value;
  if (tag === null || tag === undefined || tag === '') return null;
  if (typeof tag !== 'number' || Number.isNaN(tag)) return { min: { min: 1, actual: tag } };
  if (tag < 1) return { min: { min: 1, actual: tag } };
  if (tag > 254) return { max: { max: 254, actual: tag } };
  if (SERVER_OWNED_TAGS.includes(tag)) return { reservedTag: { tag } };
  return null;
}

/**
 * This function throws when a hex string holds more than 255 bytes, with a reason the forms
 * prefix with the row number and tag.
 */
export function assertDhcpv4OptionValueLength(valueHex: string): void {
  const byteCount = valueHex.length / 2;
  if (byteCount > DHCPV4_MAX_OPTION_VALUE_BYTES) {
    throw new Error(
      `the value is ${byteCount} bytes; a DHCPv4 option value is at most ${DHCPV4_MAX_OPTION_VALUE_BYTES} bytes.`,
    );
  }
}
