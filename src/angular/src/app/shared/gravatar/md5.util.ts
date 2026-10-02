/**
 * This module computes an MD5 digest, written from the algorithm in RFC 1321. The browser's Web
 * Crypto API does not offer MD5.
 *
 * The only caller is {@link buildGravatarUrl} (`gravatar.util.ts`): Gravatar looks avatars up by
 * the MD5 of the e-mail address. MD5's weaknesses do not matter here, since the hash is only a
 * lookup key and protects nothing.
 */

/** Per-round left-rotation amounts, 4 groups of 16, defined by RFC 1321 §3.4. */
const SHIFT_AMOUNTS = [
  7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14,
  20, 5, 9, 14, 20, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6,
  10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
];

/** Per-round additive constants, `floor(abs(sin(i + 1)) * 2^32)` for `i` in `[0, 63]`, per RFC 1321 §3.4. */
const ADDITIVE_CONSTANTS = Array.from({ length: 64 }, (_, index) =>
  Math.floor(Math.abs(Math.sin(index + 1)) * 2 ** 32),
);

/** Adds two 32-bit values modulo 2^32, as RFC 1321 requires. */
function addWrapped(a: number, b: number): number {
  return (a + b) | 0;
}

/** Rotates a 32-bit value left by `amount` bits. */
function rotateLeft(value: number, amount: number): number {
  return (value << amount) | (value >>> (32 - amount));
}

/**
 * This function computes the MD5 digest of `input`, encoded as UTF-8 (as Gravatar hashes the
 * address), and returns it as 32 lowercase hex digits.
 */
export function md5(input: string): string {
  const messageBytes = Array.from(new TextEncoder().encode(input));
  const bitLength = messageBytes.length * 8;

  // Padding (RFC 1321 §3.1-3.2): a 0x80 byte, zero bytes until the length is 56 modulo 64, then
  // the bit length as a 64-bit little-endian integer. Only the low 32 bits are written; the high
  // ones are zero for any input under 512 MiB.
  const paddedBytes = [...messageBytes, 0x80];
  while (paddedBytes.length % 64 !== 56) paddedBytes.push(0);
  for (let shift = 0; shift < 32; shift += 8) paddedBytes.push((bitLength >>> shift) & 0xff);
  for (let shift = 0; shift < 32; shift += 8) paddedBytes.push(0);

  // Initial digest state (RFC 1321 §3.3), the four 32-bit words A/B/C/D.
  let a0 = 0x67452301;
  let b0 = 0xefcdab89;
  let c0 = 0x98badcfe;
  let d0 = 0x10325476;

  for (let blockStart = 0; blockStart < paddedBytes.length; blockStart += 64) {
    const words = new Array<number>(16);
    for (let wordIndex = 0; wordIndex < 16; wordIndex += 1) {
      const byteOffset = blockStart + wordIndex * 4;
      words[wordIndex] =
        paddedBytes[byteOffset] |
        (paddedBytes[byteOffset + 1] << 8) |
        (paddedBytes[byteOffset + 2] << 16) |
        (paddedBytes[byteOffset + 3] << 24);
    }

    let a = a0;
    let b = b0;
    let c = c0;
    let d = d0;

    for (let i = 0; i < 64; i += 1) {
      let f: number;
      let g: number;

      if (i < 16) {
        f = (b & c) | (~b & d);
        g = i;
      } else if (i < 32) {
        f = (d & b) | (~d & c);
        g = (5 * i + 1) % 16;
      } else if (i < 48) {
        f = b ^ c ^ d;
        g = (3 * i + 5) % 16;
      } else {
        f = c ^ (b | ~d);
        g = (7 * i) % 16;
      }

      f = addWrapped(addWrapped(addWrapped(f, a), ADDITIVE_CONSTANTS[i]), words[g]);
      a = d;
      d = c;
      c = b;
      b = addWrapped(b, rotateLeft(f, SHIFT_AMOUNTS[i]));
    }

    a0 = addWrapped(a0, a);
    b0 = addWrapped(b0, b);
    c0 = addWrapped(c0, c);
    d0 = addWrapped(d0, d);
  }

  return [a0, b0, c0, d0].map(toLittleEndianHex).join('');
}

/** Renders a 32-bit word as 8 lowercase hex digits, low byte first, as RFC 1321 outputs the digest. */
function toLittleEndianHex(word: number): string {
  const bytes = [word & 0xff, (word >>> 8) & 0xff, (word >>> 16) & 0xff, (word >>> 24) & 0xff];
  return bytes.map((byte) => byte.toString(16).padStart(2, '0')).join('');
}
