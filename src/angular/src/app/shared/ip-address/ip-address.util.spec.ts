import { FormControl } from '@angular/forms';
import { ipv6AddressValidator, isIpv6Address } from './ip-address.util';

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
