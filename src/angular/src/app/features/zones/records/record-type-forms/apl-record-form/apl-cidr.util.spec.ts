import { base64ToHex, formatCidr, parseCidr } from './apl-cidr.util';

/** These tests cover CIDR parsing and formatting for APL items, IPv4 and IPv6. Plain unit tests. */
describe('apl-cidr.util', () => {
  it('parses an IPv4 /24 CIDR into 3 octets when none of them is a trailing zero', () => {
    const parts = parseCidr('192.0.2.0/24');
    expect(parts.addressFamily).toBe(1);
    expect(parts.prefixLength).toBe(24);
    // 192.0.2 -> base64.
    expect(parts.addressFamilyDependentPart).toBe(btoa(String.fromCharCode(192, 0, 2)));
  });

  it('drops the trailing zero octets of an IPv4 /24 (RFC 3123 Section 4)', () => {
    expect(parseCidr('10.0.0.0/24').addressFamilyDependentPart).toBe(btoa('\x0a'));
  });

  it('clears the bits after the prefix inside the last partial octet', () => {
    expect(parseCidr('10.1.255.0/20').addressFamilyDependentPart).toBe(btoa('\x0a\x01\xf0'));
  });

  it('sends no octets for the /0 prefix', () => {
    expect(parseCidr('0.0.0.0/0').addressFamilyDependentPart).toBe('');
  });

  it('sends only the 4 octets an IPv6 /64 prefix covers when the others are zero', () => {
    expect(parseCidr('2001:db8::/64').addressFamilyDependentPart).toBe(btoa('\x20\x01\x0d\xb8'));
  });

  it('keeps an interior zero octet that is followed by a non-zero one', () => {
    expect(parseCidr('192.0.2.0/24').addressFamilyDependentPart).toBe(btoa('\xc0\x00\x02'));
  });

  it('formats a canonical item back to the CIDR the operator typed', () => {
    expect(formatCidr(parseCidr('10.0.0.0/24'))).toBe('10.0.0.0/24');
    expect(formatCidr(parseCidr('10.1.255.0/20'))).toBe('10.1.240.0/20');
  });

  it('round-trips an IPv4 CIDR through parseCidr/formatCidr', () => {
    expect(formatCidr(parseCidr('192.0.2.0/24'))).toBe('192.0.2.0/24');
    expect(formatCidr(parseCidr('10.1.2.3/32'))).toBe('10.1.2.3/32');
  });

  it('round-trips a well-known IPv6 documentation CIDR through parseCidr/formatCidr', () => {
    const parts = parseCidr('2001:db8::/32');
    expect(parts.addressFamily).toBe(2);
    expect(parts.prefixLength).toBe(32);
    expect(formatCidr(parts)).toBe('2001:db8:0:0:0:0:0:0/32');
  });

  it('round-trips a fully-expanded IPv6 address with no "::" compression', () => {
    const parts = parseCidr('2001:0db8:0000:0000:0000:0000:0000:0001/128');
    expect(formatCidr(parts)).toBe('2001:db8:0:0:0:0:0:1/128');
  });

  it('throws for a malformed address', () => {
    expect(() => parseCidr('not-an-address/24')).toThrow();
  });

  it('throws for a prefix length out of range', () => {
    expect(() => parseCidr('192.0.2.0/33')).toThrow();
    expect(() => parseCidr('2001:db8::/129')).toThrow();
  });

  it('renders base64 bytes as lowercase hex', () => {
    expect(base64ToHex(btoa(String.fromCharCode(0x0a, 0x01, 0xf0)))).toBe('0a01f0');
    expect(base64ToHex('')).toBe('');
  });
});
