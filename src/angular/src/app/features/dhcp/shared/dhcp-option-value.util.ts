/**
 * This module converts between a DHCP option's display value (an IP address, a number, a
 * checkbox) and the uppercase hex string stored in the option's `ValueHex`. It lets the scope and
 * tag rule forms show a typed field for an option the catalog knows, instead of a raw hex box.
 *
 * <p>The backend does not interpret `ValueHex` by type (its option catalog is metadata only), so
 * this module is the only place that does. When a stored value does not fit its catalog type, the
 * forms catch the decode error and show that row as raw hex.
 */
import { DhcpOptionValueTypeEnum } from '../../../api-client';

/** This function splits one-entry-per-line text into trimmed, non-empty entries. */
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
 * This function parses an IPv6 address, with optional `::` compression (RFC 4291 §2.2), into 16
 * octets. The zones feature's `apl-cidr.util.ts` has the same parser; this copy keeps the DHCP
 * feature free of a cross-feature import.
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

/** This function formats 16 octets as an IPv6 address without `::` compression. */
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
 * This function decodes an even-length hex string into bytes. It accepts the same input as the
 * backend's `Convert.FromHexString` check, so a value it accepts is not rejected on save.
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

/** The longest label of a domain name, in octets (RFC 1035 §2.3.4). */
const MAX_LABEL_OCTETS = 63;

/** The longest wire form of one domain name, in octets, with length bytes and root label. */
const MAX_NAME_OCTETS = 255;

/**
 * This function encodes a list of domain names (one per line) in the uncompressed RFC 1035 §3.1
 * wire form, back to back (RFC 3646 §4). It mirrors the backend `DhcpDomainNameListCodec`, so a
 * value the form accepts is accepted on save: each name is trimmed, one trailing dot is optional,
 * labels are 1-63 printable ASCII octets and a name is at most 255 octets on the wire.
 */
function encodeDomainNameList(rawValue: string): number[] {
  const names = splitLines(rawValue);

  if (names.length === 0) throw new Error('A domain name list needs at least one name.');

  const bytes: number[] = [];

  for (const rawName of names) {
    const name = rawName.endsWith('.') ? rawName.slice(0, -1) : rawName;

    if (name.length === 0) throw new Error('A domain name cannot be empty.');

    const start = bytes.length;

    for (const label of name.split('.')) {
      if (label.length === 0) throw new Error(`The domain name "${rawName}" has an empty label.`);

      if (label.length > MAX_LABEL_OCTETS) {
        throw new Error(
          `A label of the domain name "${rawName}" is longer than ${MAX_LABEL_OCTETS} octets.`,
        );
      }

      bytes.push(label.length);

      for (let i = 0; i < label.length; i++) {
        const code = label.charCodeAt(i);
        // Printable ASCII only: the bytes are written as-is.
        if (code < 0x20 || code > 0x7e) {
          throw new Error(
            `The domain name "${rawName}" contains a character that is not printable ASCII.`,
          );
        }
        bytes.push(code);
      }
    }

    bytes.push(0);

    if (bytes.length - start > MAX_NAME_OCTETS) {
      throw new Error(
        `The domain name "${rawName}" is longer than ${MAX_NAME_OCTETS} octets on the wire.`,
      );
    }
  }

  return bytes;
}

/**
 * This function decodes the wire form of a domain name list into one dot-joined name per line. It
 * throws on a compression pointer or reserved label type, a label that runs past the end, a name
 * without a root label, a name over 255 octets, a non-printable byte or an empty value, so the
 * caller shows the row as hex.
 */
function decodeDomainNameList(bytes: readonly number[]): string {
  if (bytes.length === 0) throw new Error('A domain name list needs at least one name.');

  const names: string[] = [];
  let offset = 0;

  while (offset < bytes.length) {
    const nameStart = offset;
    const labels: string[] = [];
    let sawRootLabel = false;

    while (offset < bytes.length) {
      const labelLength = bytes[offset++];

      // Either top bit set is a compression pointer or reserved label type; DHCPv6 names are
      // never compressed.
      if ((labelLength & 0xc0) !== 0) {
        throw new Error('The domain name list uses a compressed or reserved label.');
      }

      if (labelLength === 0) {
        sawRootLabel = true;
        break;
      }

      if (offset + labelLength > bytes.length) {
        throw new Error('A label runs past the end of the domain name list.');
      }

      const label = bytes.slice(offset, offset + labelLength);

      if (label.some((byte) => byte < 0x20 || byte > 0x7e)) {
        throw new Error('A label of the domain name list is not printable ASCII.');
      }

      labels.push(String.fromCharCode(...label));
      offset += labelLength;
    }

    if (!sawRootLabel) throw new Error('A domain name has no terminating root label.');

    if (offset - nameStart > MAX_NAME_OCTETS) {
      throw new Error(`A domain name is longer than ${MAX_NAME_OCTETS} octets.`);
    }

    names.push(labels.join('.'));
  }

  return names.join('\n');
}

/**
 * This function encodes a display value into the hex string stored in `ValueHex`, the inverse of
 * {@link decodeDhcpOptionValue}. It throws when the value is invalid. A `null` `valueType` (an
 * option not in the catalog) is treated as {@link DhcpOptionValueTypeEnum.OpaqueHex}: the typed
 * text is the hex value, only checked and uppercased.
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

    // A Duration is an unsigned 32-bit count of seconds (e.g. RFC 2132 IP Address Lease Time),
    // encoded like UInt32.
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

    case DhcpOptionValueTypeEnum.DomainNameList:
      return bytesToHex(encodeDomainNameList(rawValue));

    case DhcpOptionValueTypeEnum.OpaqueHex:
    default:
      return bytesToHex(hexToBytes(trimmed));
  }
}

/**
 * This function decodes a stored `ValueHex` into the display value for `valueType`. It throws,
 * rather than returning a wrong value, when the bytes do not fit the type (e.g. not 4 bytes for
 * {@link DhcpOptionValueTypeEnum.Ipv4Address}); the caller then shows the row as raw hex.
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

    case DhcpOptionValueTypeEnum.DomainNameList:
      return decodeDomainNameList(bytes);

    case DhcpOptionValueTypeEnum.OpaqueHex:
    default:
      return bytesToHex(bytes);
  }
}
