/**
 * This module converts between CIDR text (`192.0.2.0/24`, `2001:db8::/32`) and the APL item shape
 * `AplRecordFormComponent` sends, for IPv4 and IPv6. The backend's `AplItem` stores the address as
 * a short `byte[]` (`AddressFamilyDependentPart`), which would be unusable to type by hand.
 */

/** This interface is the APL item shape {@link parseCidr} produces. */
export interface AplCidrParts {
  /** The IANA address family number: 1 for IPv4, 2 for IPv6. */
  addressFamily: number;
  /** The prefix length, in bits. */
  prefixLength: number;
  /** The bytes the prefix covers (`ceil(prefixLength / 8)` of them), base64-encoded. */
  addressFamilyDependentPart: string;
}

/** This function parses a dotted-decimal IPv4 address into its 4 octets. */
function parseIpv4(address: string): number[] {
  const parts = address.split('.');

  if (parts.length !== 4) {
    throw new Error(`"${address}" is not a valid IPv4 address.`);
  }

  return parts.map((part) => {
    const value = Number(part);
    if (!Number.isInteger(value) || value < 0 || value > 255) {
      throw new Error(`"${address}" is not a valid IPv4 address.`);
    }
    return value;
  });
}

/** This function formats 4 octets into a dotted-decimal IPv4 address. */
function formatIpv4(bytes: number[]): string {
  return bytes.join('.');
}

/**
 * This function parses an IPv6 address, with optional `::` compression (RFC 4291 §2.2), into its
 * 16 octets. An embedded IPv4 tail (`::ffff:192.0.2.1`) is not supported.
 */
function parseIpv6(address: string): number[] {
  // A valid address contains "::" at most once.
  const halves = address.split('::');

  if (halves.length > 2) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // The groups before and after "::" (either side may be empty, as in "::1"); the zero groups it
  // stands for are inserted between them below.
  const head = halves[0].length > 0 ? halves[0].split(':') : [];
  const tail = halves.length === 2 && halves[1].length > 0 ? halves[1].split(':') : [];
  const missingGroupCount = 8 - head.length - tail.length;

  // Without "::" all 8 groups must be written out.
  if (halves.length === 1 && missingGroupCount !== 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // A negative count means more than 8 groups were written.
  if (missingGroupCount < 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // Insert the zero groups where "::" was, then check the total again.
  const groups = [...head, ...Array<string>(missingGroupCount).fill('0'), ...tail];

  if (groups.length !== 8) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // Each 16-bit group becomes two bytes in network order (high byte first).
  const bytes: number[] = [];

  for (const group of groups) {
    const value = Number.parseInt(group, 16);
    if (!Number.isInteger(value) || value < 0 || value > 0xffff || group.length === 0) {
      throw new Error(`"${address}" is not a valid IPv6 address.`);
    }
    bytes.push((value >> 8) & 0xff, value & 0xff);
  }

  return bytes;
}

/** This function formats 16 octets into a full-form (non-compressed) IPv6 address. */
function formatIpv6(bytes: number[]): string {
  const groups: string[] = [];

  for (let i = 0; i < 16; i += 2) {
    groups.push(((bytes[i] << 8) | bytes[i + 1]).toString(16));
  }

  return groups.join(':');
}

/** This function base64-encodes a plain byte array. */
function bytesToBase64(bytes: number[]): string {
  let binary = '';
  bytes.forEach((byte) => (binary += String.fromCharCode(byte)));
  return btoa(binary);
}

/** This function decodes a base64 string back into a plain byte array. */
function base64ToBytes(base64: string): number[] {
  const binary = atob(base64);
  const bytes: number[] = [];

  for (let i = 0; i < binary.length; i++) {
    bytes.push(binary.charCodeAt(i));
  }

  return bytes;
}

/**
 * This function parses a CIDR string (`192.0.2.0/24` or `2001:db8::/32`) into an address family,
 * prefix length and the address bytes the prefix covers. It throws on a malformed address or an
 * out-of-range prefix length.
 */
export function parseCidr(cidr: string): AplCidrParts {
  const [address, prefixText] = cidr.split('/');
  const isIpv6 = address.includes(':');
  const fullBytes = isIpv6 ? parseIpv6(address) : parseIpv4(address);
  const maxPrefixLength = isIpv6 ? 128 : 32;
  const prefixLength = Number(prefixText);

  if (!Number.isInteger(prefixLength) || prefixLength < 0 || prefixLength > maxPrefixLength) {
    throw new Error(`"${cidr}" does not have a valid prefix length.`);
  }

  // Keep only the bytes the prefix covers (a /24 keeps 3 octets), rounding up so a /20 keeps its
  // partial byte. Trailing zero bytes inside the prefix are kept.
  const significantByteCount = Math.ceil(prefixLength / 8);

  return {
    addressFamily: isIpv6 ? 2 : 1,
    prefixLength,
    addressFamilyDependentPart: bytesToBase64(fullBytes.slice(0, significantByteCount)),
  };
}

/**
 * This function formats an {@link AplCidrParts} as a CIDR string, padding the stored bytes with
 * zeros to the full address length. IPv6 is written uncompressed.
 */
export function formatCidr(parts: AplCidrParts): string {
  const fullLength = parts.addressFamily === 2 ? 16 : 4;
  const significantBytes = base64ToBytes(parts.addressFamilyDependentPart);
  const paddedBytes = [
    ...significantBytes,
    ...Array<number>(fullLength - significantBytes.length).fill(0),
  ];

  const address = parts.addressFamily === 2 ? formatIpv6(paddedBytes) : formatIpv4(paddedBytes);
  return `${address}/${parts.prefixLength}`;
}
