/**
 * This module contains the CIDR-text parsing/formatting logic `AplRecordFormComponent` uses,
 * kept in its own file (rather than inline in the component) since it is real, non-trivial,
 * independently testable logic in its own right: `Ninlil.Dns.Common.Wire.Records.AplItem`'s
 * own `AddressFamilyDependentPart` is a trimmed-to-significant-bytes `byte[]` (its own doc comment
 * gives the exact example -- a /24 IPv4 prefix carries only its first three octets, not all four),
 * which is technically correct to show as a raw base64/hex field but a genuinely worse operator
 * experience than accepting the same plain CIDR notation (`192.0.2.0/24`) any real router/firewall
 * configuration already uses -- this module is what makes that translation, in both directions,
 * for both IPv4 and IPv6.
 */

/** This interface is the address-family-numbered, wire-shaped form {@link parseCidr} produces. */
export interface AplCidrParts {
  /** The IANA address family number -- 1 for IPv4, 2 for IPv6. */
  addressFamily: number;
  /** The prefix length, in bits. */
  prefixLength: number;
  /** The address's significant bytes, base64-encoded, with trailing zero bytes trimmed. */
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
 * This function parses an IPv6 address (with optional `::` zero-run compression, per RFC 4291
 * §2.2) into its 16 octets. Deliberately does not support an embedded trailing IPv4 address
 * (`::ffff:192.0.2.1`) -- out of scope for this feature's own confirmed v1 APL support.
 */
function parseIpv6(address: string): number[] {
  // Splitting on "::" finds at most one zero-run compression point -- a genuine address can
  // never contain "::" twice, so more than one split half here means the input is malformed
  // rather than merely uncompressed.
  const halves = address.split('::');

  if (halves.length > 2) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // The groups before and after "::" (empty when the address starts/ends with it, e.g. "::1")
  // are collected separately so the run of implicit zero groups it represents can be reinserted
  // between them below, rather than needing its own explicit "0000" text anywhere in the input.
  const head = halves[0].length > 0 ? halves[0].split(':') : [];
  const tail = halves.length === 2 && halves[1].length > 0 ? halves[1].split(':') : [];
  const missingGroupCount = 8 - head.length - tail.length;

  // With no "::" present at all, every one of the address's 8 groups must already be spelled out
  // explicitly -- a head/tail count that doesn't already sum to 8 here means a group was omitted
  // without the compression marker that would justify it.
  if (halves.length === 1 && missingGroupCount !== 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // A negative count means head+tail already named more than 8 groups on their own -- too many
  // groups even before accounting for whatever "::" was meant to stand in for.
  if (missingGroupCount < 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // Reassemble the full 8-group address by splicing the implicit zero-run back in at the point
  // "::" occupied, then re-validate the total in case "::" appeared but stood for zero actual
  // missing groups (e.g. every group already spelled out with "::" only at one end).
  const groups = [...head, ...Array<string>(missingGroupCount).fill('0'), ...tail];

  if (groups.length !== 8) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  // Each 16-bit hex group becomes two bytes, high byte first, matching the address's own
  // on-the-wire network byte order.
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
 * This function parses a plain CIDR string (`192.0.2.0/24` or `2001:db8::/32`) into the
 * address-family/prefix-length/trimmed-significant-bytes shape `AplItem` itself stores.
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

  // Only the bytes the prefix length actually covers are significant -- a /24 needs its first 3
  // octets, not all 4/16 -- rounding up so a prefix that ends mid-byte (e.g. /20) still keeps
  // that partial byte rather than truncating it away.
  const significantByteCount = Math.ceil(prefixLength / 8);

  return {
    addressFamily: isIpv6 ? 2 : 1,
    prefixLength,
    addressFamilyDependentPart: bytesToBase64(fullBytes.slice(0, significantByteCount)),
  };
}

/**
 * This function formats an {@link AplCidrParts} back into a plain CIDR string, zero-padding
 * `addressFamilyDependentPart`'s own trimmed bytes back out to the address family's full length
 * first.
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
