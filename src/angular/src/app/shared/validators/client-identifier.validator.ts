import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * The largest client identifier, in bytes, the server stores. Its normal form (two digits per
 * byte plus a colon between bytes) must fit the 256-character identifier columns: 85 bytes are
 * 254 characters. Mirrors `ClientIdentifierFormat.MaximumBytes` in `Ninlil.Dns.Common`.
 */
export const CLIENT_IDENTIFIER_MAXIMUM_BYTES = 85;

const HEX_DIGITS = /^[0-9a-fA-F]+$/;

/** This function reports how many bytes a list of hex groups holds, or `null` when a group is malformed. */
function countBytes(groups: string[], minimumDigits: number, maximumDigits: number): number | null {
  let bytes = 0;
  for (const group of groups) {
    if (group.length < minimumDigits || group.length > maximumDigits) return null;
    if (!HEX_DIGITS.test(group)) return null;
    bytes += group.length === 4 ? 2 : 1;
  }
  return bytes;
}

/**
 * This function reports whether text is a client identifier (a MAC address, a DHCPv4
 * Client-identifier or a DUID) in a form the server accepts. It mirrors
 * `ClientIdentifierFormat.TryNormalize` in `Ninlil.Dns.Common` rule for rule, so the form refuses
 * exactly what the API would answer with a 400.
 *
 * <p>After trimming surrounding white space, the accepted forms are: groups of one or two hex
 * digits separated by `:` or by `-` (one kind per value; `0:1b:63:84:45:e6` is fine), groups of
 * exactly four hex digits separated by `.` (`aabb.ccdd.eeff`), and an unseparated even-length hex
 * string. Hex digits may be either case. The value must hold 1 to
 * {@link CLIENT_IDENTIFIER_MAXIMUM_BYTES} bytes. Mixed separators, odd unseparated length, a `0x`
 * prefix and empty groups are refused.
 */
export function isClientIdentifier(value: string | null | undefined): boolean {
  const text = (value ?? '').trim();
  if (text.length === 0) return false;

  let bytes: number | null;
  if (text.includes('.')) {
    bytes = countBytes(text.split('.'), 4, 4);
  } else if (text.includes(':') || text.includes('-')) {
    if (text.includes(':') && text.includes('-')) return false;
    bytes = countBytes(text.split(text.includes(':') ? ':' : '-'), 1, 2);
  } else {
    if (text.length % 2 !== 0) return false;
    bytes = countBytes(text.match(/../g) ?? [], 2, 2);
  }

  return bytes !== null && bytes >= 1 && bytes <= CLIENT_IDENTIFIER_MAXIMUM_BYTES;
}

/**
 * This validator factory returns a validator for a client identifier control. A value that is not
 * an identifier (see {@link isClientIdentifier}) sets the `clientIdentifier` error. An empty value
 * passes, so `Validators.required` keeps its own, clearer message for it. Disabled controls are
 * skipped by Angular, which suits the client form's locked identifier in edit mode.
 */
export function clientIdentifierValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value as string | null | undefined;
    if (value === null || value === undefined || value.trim().length === 0) return null;
    return isClientIdentifier(value) ? null : { clientIdentifier: true };
  };
}

/** The hint shown under each client identifier field: the accepted forms and the stored form. */
export const CLIENT_IDENTIFIER_HINT =
  'MAC address or DUID in hex, for example AA:BB:CC:DD:EE:FF, aa-bb-cc-dd-ee-ff or ' +
  'aabb.ccdd.eeff. Stored as aa:bb:cc:dd:ee:ff.';

/** The message shown when a client identifier control has the `clientIdentifier` error. */
export const CLIENT_IDENTIFIER_ERROR =
  'Enter a MAC address or DUID in hex: bytes separated by ":" or "-", dotted groups of four ' +
  'digits, or unseparated hex, at most 85 bytes.';
