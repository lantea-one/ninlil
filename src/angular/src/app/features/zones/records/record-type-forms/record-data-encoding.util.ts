/**
 * This module holds pure conversion helpers shared by the record-type forms for byte fields. The
 * backend reads a record's `data` with `Ninlil.Common.Serializer.JsonSerializer`, which has no
 * `byte[]` converter, so every `byte[]` on a `*RecordData` model travels as a base64 string.
 *
 * Several of those fields are normally written in hex: DS digests, SSHFP fingerprints, TLSA and
 * SMIMEA association data (as in zone files and `ssh-keygen -r` output), and the generic
 * presentation of an unknown SVCB/HTTPS parameter (RFC 9460 §2.4). {@link hexToBase64} and
 * {@link base64ToHex} let those forms show and accept hex while sending base64.
 *
 * The rule per field: **hex** for digests, hashes, salts, fingerprints and opaque data (SSHFP,
 * DS/CDS, TLSA/SMIMEA, the NSEC3/NSEC3PARAM salt, NSEC3's next hashed owner name, SVCB/HTTPS
 * parameter values), and colon-separated hex through {@link macToBase64} and {@link base64ToMac}
 * for EUI48/EUI64 identifiers. **Base64** for key and signature material that is normally shown
 * in base64 anyway (DNSKEY/CDNSKEY, RRSIG, CERT, IPSECKEY, OPENPGPKEY); those forms use a plain
 * base64 textarea and need no helper.
 */

/**
 * This constant matches a hexadecimal string of whole bytes, including the empty string, since an
 * NSEC3/NSEC3PARAM salt may be empty (RFC 5155 §3.1.5).
 */
export const EVEN_LENGTH_HEX_PATTERN = /^([0-9a-fA-F]{2})*$/;

/** This constant matches a colon-separated 6-octet MAC-style address, for an EUI48 identifier. */
export const MAC48_PATTERN = /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/;

/** This constant matches a colon-separated 8-octet MAC-style address, for an EUI64 identifier. */
export const MAC64_PATTERN = /^([0-9A-Fa-f]{2}:){7}[0-9A-Fa-f]{2}$/;

/**
 * This function converts a hexadecimal string (any case, no separators) into the base64 the API
 * expects for a `byte[]` field.
 */
export function hexToBase64(hex: string): string {
  const clean = hex.trim();
  const byteCount = clean.length / 2;
  const bytes = new Uint8Array(byteCount);

  // Callers validate with EVEN_LENGTH_HEX_PATTERN first, so there is no odd trailing digit.
  for (let i = 0; i < byteCount; i++) {
    bytes[i] = Number.parseInt(clean.substring(i * 2, i * 2 + 2), 16);
  }

  // `btoa` takes a "binary string" with one char code per byte, not bytes or decoded text.
  let binary = '';
  bytes.forEach((byte) => (binary += String.fromCharCode(byte)));
  return btoa(binary);
}

/** This function converts a `byte[]` field's base64 into lowercase hexadecimal for display. */
export function base64ToHex(base64: string): string {
  if (base64.trim().length === 0) return '';

  const binary = atob(base64);
  let hex = '';

  // Each char code of the binary string is one byte.
  for (let i = 0; i < binary.length; i++) {
    hex += binary.charCodeAt(i).toString(16).padStart(2, '0');
  }

  return hex;
}

/** This function converts a colon-separated EUI48/EUI64 identifier into the base64 the API expects. */
export function macToBase64(mac: string): string {
  return hexToBase64(mac.split(':').join(''));
}

/** This function converts an EUI48/EUI64 identifier's base64 into colon-separated hex for display. */
export function base64ToMac(base64: string): string {
  const hex = base64ToHex(base64);
  const pairs: string[] = [];

  for (let i = 0; i < hex.length; i += 2) {
    pairs.push(hex.substring(i, i + 2));
  }

  return pairs.join(':');
}
