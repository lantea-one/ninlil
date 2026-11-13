import { FormControl } from '@angular/forms';
import { ipv6AddressValidator, isIpv6Address, parseIpv6 } from './ip-address.util';

/**
 * These tests cover the shared IPv6 text check against the three forms of RFC 4291 §2.2 and the
 * malformed shapes the records API refuses. Plain functions, no TestBed.
 */
describe('isIpv6Address', () => {
  for (const address of [
    '::',
    '::1',
    '2001:db8::1',
    '2001:DB8:0:0:8:800:200C:417A',
    '1:2:3:4:5:6:7:8',
    '1:2:3:4:5:6:7::',
    '::2:3:4:5:6:7:8',
    '::ffff:192.0.2.1',
    '64:ff9b::192.0.2.33',
    '0:0:0:0:0:FFFF:129.144.52.38',
    '::13.1.68.3',
  ]) {
    it(`accepts ${address}`, () => {
      expect(isIpv6Address(address)).toBeTrue();
    });
  }

  for (const address of [
    '',
    '1:2:3:4:5:6:7:8:9',
    '1:2:3:4:5:6:7',
    '::ffff:192.0.2.256',
    '::192.0.2.1:1',
    '1:2:3:4:5:6:7:1.2.3.4',
    'fe80::1%2',
    '1::2::3',
    ':::',
    ':1:2:3:4:5:6:7',
    '1:2:3:4:5:6:7:',
    '12345::1',
    'g::1',
    '1.2.3.4',
    '1:2:3:4:5:6:7::8',
    '1.2.3.4::',
    '192.0.2.1::',
    '0:1.2.3.4::',
    '0:0:192.0.2.1::',
  ]) {
    it(`rejects "${address}"`, () => {
      expect(isIpv6Address(address)).toBeFalse();
    });
  }
});

/** These tests cover the form validator built on {@link isIpv6Address}. */
describe('ipv6AddressValidator', () => {
  it('passes an empty value so the required validator owns that message', () => {
    expect(ipv6AddressValidator(new FormControl(''))).toBeNull();
  });

  it('flags a malformed value with the invalidIpv6 error', () => {
    expect(ipv6AddressValidator(new FormControl('1::2::3'))).toEqual({ invalidIpv6: true });
  });

  it('passes an IPv4-embedded address', () => {
    expect(ipv6AddressValidator(new FormControl('::ffff:192.0.2.1'))).toBeNull();
  });
});

/** This function writes the 16 octets of a parsed address as lowercase hex, two digits each. */
function bytesHex(bytes: number[]): string {
  return bytes.map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * These tests cover `parseIpv6`, which the APL CIDR and DHCP option encoders use. Each expected
 * value is the eight 16-bit groups written as four hex digits, joined.
 */
describe('parseIpv6', () => {
  const zero = '0000';

  it('expands "::" alone to eight zero groups', () => {
    expect(bytesHex(parseIpv6('::'))).toBe(
      [zero, zero, zero, zero, zero, zero, zero, zero].join(''),
    );
  });

  it('expands "::" at the start', () => {
    expect(bytesHex(parseIpv6('::1'))).toBe(
      [zero, zero, zero, zero, zero, zero, zero, '0001'].join(''),
    );
  });

  it('expands "::" in the middle', () => {
    expect(bytesHex(parseIpv6('2001:db8::1'))).toBe(
      ['2001', '0db8', zero, zero, zero, zero, zero, '0001'].join(''),
    );
  });

  it('expands "::" at the end', () => {
    expect(bytesHex(parseIpv6('1:2:3:4:5:6:7::'))).toBe(
      ['0001', '0002', '0003', '0004', '0005', '0006', '0007', zero].join(''),
    );
  });

  it('parses the full form with upper-case digits', () => {
    expect(bytesHex(parseIpv6('2001:DB8:0:0:8:800:200C:417A'))).toBe(
      ['2001', '0db8', zero, zero, '0008', '0800', '200c', '417a'].join(''),
    );
  });

  it('parses an IPv4-embedded address after "::"', () => {
    expect(bytesHex(parseIpv6('::ffff:192.0.2.1'))).toBe(
      [zero, zero, zero, zero, zero, 'ffff', 'c000', '0201'].join(''),
    );
  });

  it('parses an IPv4 tail after a prefix and "::"', () => {
    expect(bytesHex(parseIpv6('64:ff9b::192.0.2.33'))).toBe(
      ['0064', 'ff9b', zero, zero, zero, zero, 'c000', '0221'].join(''),
    );
  });

  it('parses an IPv4 tail with "::" alone before it', () => {
    expect(bytesHex(parseIpv6('::192.0.2.1'))).toBe(
      [zero, zero, zero, zero, zero, zero, 'c000', '0201'].join(''),
    );
  });

  it('parses an IPv4 tail that fills the last two groups of the full form', () => {
    expect(bytesHex(parseIpv6('1:2:3:4:5:6:1.2.3.4'))).toBe(
      ['0001', '0002', '0003', '0004', '0005', '0006', '0102', '0304'].join(''),
    );
  });

  for (const address of [
    '1:2:3:4:5:6:7:8:9',
    '1:2:3:4:5:6:7',
    '::ffff:192.0.2.256',
    '::192.0.2.1:1',
    '::192.0.2.1.5',
    '::192.0.2',
    '::192.0.2.+1',
    '::192.0.2.0004',
    '::192.0.2. 1',
    '::-1.0.2.1',
    '1.2.3.4::',
    '1.2.3.4:5',
    '1:2:3:4:5:6:7:1.2.3.4',
    '1::2::3',
    ':::',
    ':1:2:3:4:5:6:7',
    '1:2:3:4:5:6:7:',
    '1:2:3:4:5:6:7::8',
    '1:2:3:4:5:6:7:8::',
    '12345::1',
    'g::1',
    '12g::1',
    ' 1::2',
    '+1::2',
    '1.2.3.4',
    'fe80::1%2',
    '',
  ]) {
    it(`throws for "${address}"`, () => {
      expect(() => parseIpv6(address)).toThrowError(`"${address}" is not a valid IPv6 address.`);
    });
  }
});

/**
 * These tests check that `parseIpv6` and `isIpv6Address` give the same verdict for each text, so a
 * value the form accepts is always one the encoders can parse.
 */
describe('parseIpv6 and isIpv6Address', () => {
  for (const address of [
    '::',
    '::1',
    '2001:db8::1',
    '1:2:3:4:5:6:7::',
    '::ffff:192.0.2.1',
    '64:ff9b::192.0.2.33',
    '::192.0.2.1',
    '1:2:3:4:5:6:1.2.3.4',
    '1:2:3:4:5:6:7:1.2.3.4',
    '::192.0.2.256',
    '1:2:3:4:5:6:7:8::',
    '12g::1',
    '+1::2',
    ':::',
    '1::2::3',
    '1.2.3.4',
    '1.2.3.4::',
    '0:0:192.0.2.1::',
  ]) {
    it(`gives the same verdict for "${address}"`, () => {
      let parses = true;
      try {
        parseIpv6(address);
      } catch {
        parses = false;
      }
      expect(parses).toBe(isIpv6Address(address));
    });
  }
});
