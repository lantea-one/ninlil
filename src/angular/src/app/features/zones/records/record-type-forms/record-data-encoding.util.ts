/**
 * This module contains small, pure conversion helpers several record-type forms share, for a
 * problem that only some record types actually have: `RecordsController`'s own
 * `TryEncodeData`/`ToCamelCaseDataJson` decode a record's `data` field through
 * `Ninlil.Common.Serializer.JsonSerializer`,
 * whose options register no `byte[]` converter of any kind -- so every `byte[]` property on a
 * `Ninlil.Dns.Common.Wire.Records.*RecordData` model (confirmed by reading each one directly,
 * not assumed) serializes to and from plain JSON exactly the way the .NET runtime's own default
 * `System.Text.Json` behavior does for an untyped `byte[]`: a base64 string.
 *
 * A raw base64 text box is technically correct for *every* such field, but it is genuinely worse
 * UX than this project needs to settle for, since several of these fields have a well-established
 * real-world presentation convention that is not base64 at all -- a DS record's own digest, an
 * SSHFP fingerprint, and a TLSA/SMIMEA certificate association datum are all conventionally shown
 * as plain hexadecimal in zone files, DNS registrar UIs, and `ssh-keygen -r`'s own output, and
 * RFC 9460 §2.4's own "generic key definition" presentation form for an SVCB/HTTPS parameter this
 * UI does not specifically know the shape of is *also* hex, not base64. This module's own
 * {@link hexToBase64}/{@link base64ToHex} let each of those forms show and accept hex while still
 * producing the exact base64 the wire format actually needs underneath.
 *
 * The dividing line this module draws, deliberately, field type by field type: **hex** for
 * anything that is conventionally a digest, hash, salt, fingerprint, or generic/opaque binary
 * datum (SSHFP's fingerprint, DS/CDS's digest, TLSA/SMIMEA's certificate association data,
 * NSEC3/NSEC3PARAM's salt and NSEC3's next hashed owner name, an SVCB/HTTPS parameter's value,
 * and, via the MAC-address-flavored {@link macToBase64}/{@link base64ToMac}, an EUI48/EUI64
 * identifier); **base64** for anything that is conventionally genuine key or signature material
 * with its own real-world base64 presentation convention already (DNSKEY/CDNSKEY's public key,
 * RRSIG's signature, CERT's certificate, IPSECKEY's public key, OPENPGPKEY's key data) -- left as
 * a plain base64 textarea in each of those forms directly, with no helper needed here at all,
 * since base64 already *is* what this project's own wire format expects with no conversion step.
 */

/**
 * This constant is a validator pattern matching a hexadecimal string of an even length (byte
 * pairs) -- including the empty string, since several of these fields (most notably an
 * NSEC3/NSEC3PARAM salt) are legitimately allowed to be empty ("no salt configured" is a real,
 * common zone configuration, per RFC 5155 §3.1.5).
 */
export const EVEN_LENGTH_HEX_PATTERN = /^([0-9a-fA-F]{2})*$/;

/** This constant matches a colon-separated 6-octet MAC-style address, for an EUI48 identifier. */
export const MAC48_PATTERN = /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/;

/** This constant matches a colon-separated 8-octet MAC-style address, for an EUI64 identifier. */
export const MAC64_PATTERN = /^([0-9A-Fa-f]{2}:){7}[0-9A-Fa-f]{2}$/;

/**
 * This function converts a plain hexadecimal string (case-insensitive, no separators) into the
 * base64 form `RecordsController`'s own JSON encoding of a `byte[]` field actually expects.
 */
export function hexToBase64(hex: string): string {
  const clean = hex.trim();
  const byteCount = clean.length / 2;
  const bytes = new Uint8Array(byteCount);

  // Parse the hex string two characters (one byte) at a time -- EVEN_LENGTH_HEX_PATTERN already
  // guarantees an even length by the time a form actually submits this, so no partial-pair case
  // needs handling here.
  for (let i = 0; i < byteCount; i++) {
    bytes[i] = Number.parseInt(clean.substring(i * 2, i * 2 + 2), 16);
  }

  // `btoa` only accepts a "binary string" -- one UTF-16 code unit per byte, not a genuine text
  // string -- so each byte value is mapped straight to its own char code before encoding, rather
  // than handing btoa the raw bytes or a decoded-text string.
  let binary = '';
  bytes.forEach((byte) => (binary += String.fromCharCode(byte)));
  return btoa(binary);
}

/**
 * This function converts the base64 form a `byte[]` field's JSON representation actually carries
 * back into a plain, lowercase hexadecimal string for display.
 */
export function base64ToHex(base64: string): string {
  if (base64.trim().length === 0) return '';

  const binary = atob(base64);
  let hex = '';

  // The inverse of hexToBase64's own binary-string construction: each decoded char code is
  // exactly one byte's value, so it converts straight to a zero-padded two-digit hex pair with no
  // further decoding step.
  for (let i = 0; i < binary.length; i++) {
    hex += binary.charCodeAt(i).toString(16).padStart(2, '0');
  }

  return hex;
}

/**
 * This function converts a colon-separated MAC-style hexadecimal address (an EUI48/EUI64
 * identifier's own conventional presentation form) into the base64 form its `byte[]` field's JSON
 * representation actually expects.
 */
export function macToBase64(mac: string): string {
  return hexToBase64(mac.split(':').join(''));
}

/**
 * This function converts the base64 form an EUI48/EUI64 identifier's JSON representation actually
 * carries back into a colon-separated MAC-style hexadecimal address for display.
 */
export function base64ToMac(base64: string): string {
  const hex = base64ToHex(base64);
  const pairs: string[] = [];

  for (let i = 0; i < hex.length; i += 2) {
    pairs.push(hex.substring(i, i + 2));
  }

  return pairs.join(':');
}
