import {
  base64ToHex,
  base64ToMac,
  EVEN_LENGTH_HEX_PATTERN,
  hexToBase64,
  macToBase64,
  MAC48_PATTERN,
} from './record-data-encoding.util';

describe('record-data-encoding.util', () => {
  it('round-trips a hex string through hexToBase64/base64ToHex', () => {
    const hex = 'deadbeef';
    const base64 = hexToBase64(hex);
    expect(base64ToHex(base64)).toBe(hex);
  });

  it('produces the known base64 encoding of a short byte sequence', () => {
    // 0x01 0x02 0x03 is base64 "AQID".
    expect(hexToBase64('010203')).toBe('AQID');
    expect(base64ToHex('AQID')).toBe('010203');
  });

  it('treats an empty hex string as an empty base64 payload, for an optional salt field', () => {
    expect(hexToBase64('')).toBe('');
    expect(base64ToHex('')).toBe('');
  });

  it('round-trips a MAC-style address through macToBase64/base64ToMac', () => {
    const mac = '00:1a:2b:3c:4d:5e';
    expect(base64ToMac(macToBase64(mac))).toBe(mac);
  });

  it('matches EVEN_LENGTH_HEX_PATTERN against valid hex and rejects odd-length/non-hex input', () => {
    expect(EVEN_LENGTH_HEX_PATTERN.test('')).toBeTrue();
    expect(EVEN_LENGTH_HEX_PATTERN.test('deadbeef')).toBeTrue();
    expect(EVEN_LENGTH_HEX_PATTERN.test('abc')).toBeFalse();
    expect(EVEN_LENGTH_HEX_PATTERN.test('zz')).toBeFalse();
  });

  it('matches MAC48_PATTERN against a well-formed 6-octet address only', () => {
    expect(MAC48_PATTERN.test('00:1a:2b:3c:4d:5e')).toBeTrue();
    expect(MAC48_PATTERN.test('00:1a:2b:3c:4d')).toBeFalse();
    expect(MAC48_PATTERN.test('not-a-mac')).toBeFalse();
  });
});
