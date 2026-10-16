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

/**
 * The DHCPv6 option codes the engine or the relay path writes itself, with their IANA names. They
 * mirror `DhcpRawOptionRules.ReservedDhcpv6Codes` on the server. Preference (7) is absent because
 * an operator with two servers may set it.
 */
export const DHCPV6_SERVER_OWNED_CODES: Readonly<Record<number, string>> = {
  1: 'Client Identifier',
  2: 'Server Identifier',
  3: 'IA_NA',
  4: 'IA_TA',
  5: 'IA Address',
  6: 'Option Request',
  8: 'Elapsed Time',
  9: 'Relay Message',
  11: 'Authentication',
  13: 'Status Code',
  14: 'Rapid Commit',
  18: 'Interface-ID',
  19: 'Reconfigure Message',
  20: 'Reconfigure Accept',
  25: 'IA_PD',
  26: 'IA Prefix',
  37: 'Relay Agent Remote-ID',
  39: 'Client FQDN',
};

/** The largest DHCPv6 option value in bytes: the length field is two bytes (RFC 8415 §21.1). */
export const DHCPV6_MAX_OPTION_VALUE_BYTES = 65535;

/**
 * This validator accepts a DHCPv6 option code from 1 to 255 that the server does not own. The
 * forms limit the code to a byte, as the DHCPv6 forms always did. It reports `min` or `max` for a
 * code out of range and `reservedTag` (with the code and IANA name) for a reserved code. An empty
 * value is left to `Validators.required`.
 */
export function dhcpv6OptionCodeValidator(control: AbstractControl): ValidationErrors | null {
  const code = control.value;
  if (code === null || code === undefined || code === '') return null;
  if (typeof code !== 'number' || Number.isNaN(code)) return { min: { min: 1, actual: code } };
  if (code < 1) return { min: { min: 1, actual: code } };
  if (code > 255) return { max: { max: 255, actual: code } };
  if (code in DHCPV6_SERVER_OWNED_CODES) {
    return { reservedTag: { tag: code, name: DHCPV6_SERVER_OWNED_CODES[code] } };
  }
  return null;
}

/** This function throws when a hex string holds more than 65535 bytes. */
export function assertDhcpv6OptionValueLength(valueHex: string): void {
  const byteCount = valueHex.length / 2;
  if (byteCount > DHCPV6_MAX_OPTION_VALUE_BYTES) {
    throw new Error(
      `the value is ${byteCount} bytes; a DHCPv6 option value is at most ${DHCPV6_MAX_OPTION_VALUE_BYTES} bytes.`,
    );
  }
}

/**
 * This function returns a hint for a DHCPv4 option tag whose value has a quirk the catalog type
 * does not show, or `null`. Tag 66 (TFTP Server Name) is text, so "use this server's own
 * address" sends the address as dotted-quad text. Tag 150 (TFTP Server Address, RFC 5859) is an
 * address list.
 */
export function dhcpv4OptionHint(tag: number | null): string | null {
  switch (tag) {
    case 66:
      return "The server's address is sent as text (for example 192.168.1.1), because option 66 carries a name.";
    case 150:
      return 'Option 150 is a list of TFTP server addresses (RFC 5859), one address per line.';
    default:
      return null;
  }
}
