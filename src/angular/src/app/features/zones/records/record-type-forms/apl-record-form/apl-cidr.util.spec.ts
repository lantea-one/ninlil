import { formatCidr, parseCidr } from './apl-cidr.util';

describe('apl-cidr.util', () => {
  it("parses an IPv4 /24 CIDR into 3 significant bytes, per AplItem's own trailing-zero-trim rule", () => {
    const parts = parseCidr('192.0.2.0/24');
    expect(parts.addressFamily).toBe(1);
    expect(parts.prefixLength).toBe(24);
    // 192.0.2 -> base64.
    expect(parts.addressFamilyDependentPart).toBe(btoa(String.fromCharCode(192, 0, 2)));
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
});
