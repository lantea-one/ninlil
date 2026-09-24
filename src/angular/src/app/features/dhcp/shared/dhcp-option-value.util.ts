/**
 * This module translates between a DHCP option's own friendly, typed display value (an IP
 * address, a plain number, a checkbox state) and the raw, even-length uppercase hexadecimal
 * string `DhcpScopeCustomOptionEntity`/`DhcpTagRuleOptionEntity`'s own `ValueHex` field actually
 * stores and delivers on the wire -- the whole reason `DhcpOptionsFormArray` (see
 * `dhcp-option-row.util.ts`) can offer an IP-address field or a number field instead of a raw hex
 * box for a catalog-recognized option. Nothing on the backend interprets `ValueHex` by value
 * type at all (`Ninlil.Dhcp.Common.OptionCatalog`'s own catalog is metadata only, per its own
 * doc comment) -- this project's Angular workspace is the only place that ever does, so a
 * decode failure here (a stored value whose byte length doesn't match what its own catalog entry
 * expects) must never throw all the way up into a broken form; the caller always falls back to
 * plain raw-hex editing for that one row instead.
 */
import { DhcpOptionValueTypeEnum } from '../../../api-client';

/** This function splits a textarea's own one-entry-per-line text into a trimmed, non-empty-only array. */
function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
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
function formatIpv4(bytes: readonly number[]): string {
  return bytes.join('.');
}

/**
 * This function parses an IPv6 address (with optional `::` zero-run compression, per RFC 4291
 * §2.2) into its 16 octets -- the same algorithm `AplRecordFormComponent`'s own `apl-cidr.util.ts`
 * establishes for the identical parsing problem, kept as its own small copy here rather than a
 * cross-feature import so this module stays self-contained within `features/dhcp`.
 */
function parseIpv6(address: string): number[] {
  const halves = address.split('::');

  if (halves.length > 2) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  const head = halves[0].length > 0 ? halves[0].split(':') : [];
  const tail = halves.length === 2 && halves[1].length > 0 ? halves[1].split(':') : [];
  const missingGroupCount = 8 - head.length - tail.length;

  if (halves.length === 1 && missingGroupCount !== 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  if (missingGroupCount < 0) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

  const groups = [...head, ...Array<string>(missingGroupCount).fill('0'), ...tail];

  if (groups.length !== 8) {
    throw new Error(`"${address}" is not a valid IPv6 address.`);
  }

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
function formatIpv6(bytes: readonly number[]): string {
  const groups: string[] = [];

  for (let i = 0; i < 16; i += 2) {
    groups.push(((bytes[i] << 8) | bytes[i + 1]).toString(16));
  }

  return groups.join(':');
}

/** This function encodes a plain byte array as an even-length, uppercase hexadecimal string. */
function bytesToHex(bytes: readonly number[]): string {
  return bytes
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase();
}

/**
 * This function decodes an even-length hexadecimal string into its raw bytes -- the same
 * `Convert.FromHexString` shape `DhcpScopeCustomOptionsController.TryValidate` validates
 * server-side, so anything this function accepts is also something the real backend accepts.
 */
function hexToBytes(hex: string): number[] {
  const trimmed = hex.trim();

  if (trimmed.length % 2 !== 0 || !/^[0-9a-fA-F]*$/.test(trimmed)) {
    throw new Error(
      `"${hex}" is not a valid hexadecimal string -- it must have an even number of hex digits.`,
    );
  }

  const bytes: number[] = [];
  for (let i = 0; i < trimmed.length; i += 2) {
    bytes.push(Number.parseInt(trimmed.slice(i, i + 2), 16));
  }

  return bytes;
}

/** This function parses a plain decimal integer, rejecting anything outside `[min, max]` inclusive. */
function parseIntInRange(text: string, min: number, max: number): number {
  const value = Number(text);

  if (!Number.isInteger(value) || value < min || value > max) {
    throw new Error(`"${text}" must be a whole number between ${min} and ${max}.`);
  }

  return value;
}

/**
 * This function encodes a friendly, typed display value into the raw hexadecimal string this
 * option's own `ValueHex` field stores -- {@link decodeDhcpOptionValue}'s own inverse.
 * `valueType === null` (a genuinely uncataloged tag) is treated exactly like
 * {@link DhcpOptionValueTypeEnum.OpaqueHex}: the operator's own typed text is already the raw hex
 * value, unchanged (beyond validation/canonicalization) -- the same "trust the operator for a raw
 * tag" posture this entity has always had.
 */
export function encodeDhcpOptionValue(
  valueType: DhcpOptionValueTypeEnum | null,
  rawValue: string,
): string {
  const trimmed = rawValue.trim();

  switch (valueType) {
    case DhcpOptionValueTypeEnum.Ipv4Address:
      return bytesToHex(parseIpv4(trimmed));

    case DhcpOptionValueTypeEnum.Ipv4AddressList: {
      const addresses = splitLines(rawValue);
      if (addresses.length === 0) throw new Error('At least one address is required.');
      return addresses.map((address) => bytesToHex(parseIpv4(address))).join('');
    }

    case DhcpOptionValueTypeEnum.Ipv6Address:
      return bytesToHex(parseIpv6(trimmed));

    case DhcpOptionValueTypeEnum.Ipv6AddressList: {
      const addresses = splitLines(rawValue);
      if (addresses.length === 0) throw new Error('At least one address is required.');
      return addresses.map((address) => bytesToHex(parseIpv6(address))).join('');
    }

    case DhcpOptionValueTypeEnum.Boolean:
      return bytesToHex([trimmed === 'true' ? 1 : 0]);

    case DhcpOptionValueTypeEnum.UInt8:
      return bytesToHex([parseIntInRange(trimmed, 0, 0xff)]);

    case DhcpOptionValueTypeEnum.UInt16: {
      const value = parseIntInRange(trimmed, 0, 0xffff);
      return bytesToHex([(value >> 8) & 0xff, value & 0xff]);
    }

    // A Duration option is RFC 2132's own plain unsigned 32-bit seconds count (e.g. IP Address
    // Lease Time) -- the same wire shape as UInt32, just labeled differently for the operator.
    case DhcpOptionValueTypeEnum.UInt32:
    case DhcpOptionValueTypeEnum.Duration: {
      const value = parseIntInRange(trimmed, 0, 0xffffffff);
      return bytesToHex([
        (value >>> 24) & 0xff,
        (value >>> 16) & 0xff,
        (value >>> 8) & 0xff,
        value & 0xff,
      ]);
    }

    case DhcpOptionValueTypeEnum.String:
      return bytesToHex(Array.from(new TextEncoder().encode(rawValue)));

    case DhcpOptionValueTypeEnum.OpaqueHex:
    default:
      return bytesToHex(hexToBytes(trimmed));
  }
}

/**
 * This function decodes this option's own stored `ValueHex` back into the friendly, typed display
 * value {@link encodeDhcpOptionValue} produced -- throws (never silently produces a wrong value)
 * when the stored bytes don't match the shape `valueType` expects, e.g. a stored value that isn't
 * exactly 4 bytes for {@link DhcpOptionValueTypeEnum.Ipv4Address}. The caller is responsible for
 * catching that and falling back to raw-hex editing for the affected row -- see this module's own
 * doc comment for why.
 */
export function decodeDhcpOptionValue(
  valueType: DhcpOptionValueTypeEnum | null,
  valueHex: string,
): string {
  const bytes = hexToBytes(valueHex);

  switch (valueType) {
    case DhcpOptionValueTypeEnum.Ipv4Address:
      if (bytes.length !== 4) throw new Error('Expected exactly 4 bytes for an IPv4 address.');
      return formatIpv4(bytes);

    case DhcpOptionValueTypeEnum.Ipv4AddressList: {
      if (bytes.length === 0 || bytes.length % 4 !== 0) {
        throw new Error('Expected a non-empty, 4-byte-aligned IPv4 address list.');
      }
      const addresses: string[] = [];
      for (let i = 0; i < bytes.length; i += 4) addresses.push(formatIpv4(bytes.slice(i, i + 4)));
      return addresses.join('\n');
    }

    case DhcpOptionValueTypeEnum.Ipv6Address:
      if (bytes.length !== 16) throw new Error('Expected exactly 16 bytes for an IPv6 address.');
      return formatIpv6(bytes);

    case DhcpOptionValueTypeEnum.Ipv6AddressList: {
      if (bytes.length === 0 || bytes.length % 16 !== 0) {
        throw new Error('Expected a non-empty, 16-byte-aligned IPv6 address list.');
      }
      const addresses: string[] = [];
      for (let i = 0; i < bytes.length; i += 16) addresses.push(formatIpv6(bytes.slice(i, i + 16)));
      return addresses.join('\n');
    }

    case DhcpOptionValueTypeEnum.Boolean:
      if (bytes.length !== 1) throw new Error('Expected exactly 1 byte for a boolean flag.');
      return bytes[0] !== 0 ? 'true' : 'false';

    case DhcpOptionValueTypeEnum.UInt8:
      if (bytes.length !== 1) throw new Error('Expected exactly 1 byte for an 8-bit value.');
      return String(bytes[0]);

    case DhcpOptionValueTypeEnum.UInt16:
      if (bytes.length !== 2) throw new Error('Expected exactly 2 bytes for a 16-bit value.');
      return String((bytes[0] << 8) | bytes[1]);

    case DhcpOptionValueTypeEnum.UInt32:
    case DhcpOptionValueTypeEnum.Duration:
      if (bytes.length !== 4) throw new Error('Expected exactly 4 bytes for a 32-bit value.');
      return String(((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]) >>> 0);

    case DhcpOptionValueTypeEnum.String:
      return new TextDecoder().decode(new Uint8Array(bytes));

    case DhcpOptionValueTypeEnum.OpaqueHex:
    default:
      return bytesToHex(bytes);
  }
}
