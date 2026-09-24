import { DhcpOptionValueTypeEnum } from '../../../api-client';
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from './dhcp-option-value.util';

describe('dhcp-option-value.util', () => {
  describe('IPv4Address', () => {
    it('round-trips a plain address', () => {
      const hex = encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4Address, '192.168.1.1');
      expect(hex).toBe('C0A80101');
      expect(decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4Address, hex)).toBe('192.168.1.1');
    });

    it('rejects an out-of-range octet', () => {
      expect(() =>
        encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4Address, '192.168.1.999'),
      ).toThrow();
    });

    it('rejects a stored value whose byte length is wrong', () => {
      expect(() => decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4Address, 'C0A801')).toThrow();
    });
  });

  describe('IPv4AddressList', () => {
    it('round-trips several addresses, one per line', () => {
      const hex = encodeDhcpOptionValue(
        DhcpOptionValueTypeEnum.Ipv4AddressList,
        '192.168.1.1\n192.168.1.2',
      );
      expect(hex).toBe('C0A80101C0A80102');
      expect(decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4AddressList, hex)).toBe(
        '192.168.1.1\n192.168.1.2',
      );
    });

    it('rejects an empty list', () => {
      expect(() => encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv4AddressList, '')).toThrow();
    });
  });

  describe('IPv6Address', () => {
    it('round-trips a compressed address', () => {
      const hex = encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv6Address, '2001:db8::1');
      expect(hex).toBe('20010DB8000000000000000000000001');
      expect(decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv6Address, hex)).toBe(
        '2001:db8:0:0:0:0:0:1',
      );
    });

    it('rejects a malformed address', () => {
      expect(() =>
        encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv6Address, '2001::db8::1'),
      ).toThrow();
    });
  });

  describe('IPv6AddressList', () => {
    it('round-trips two addresses', () => {
      const hex = encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv6AddressList, '::1\n::2');
      const decoded = decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Ipv6AddressList, hex);
      expect(decoded.split('\n')).toEqual(['0:0:0:0:0:0:0:1', '0:0:0:0:0:0:0:2']);
    });
  });

  describe('Boolean', () => {
    it('round-trips true and false', () => {
      expect(
        decodeDhcpOptionValue(
          DhcpOptionValueTypeEnum.Boolean,
          encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Boolean, 'true'),
        ),
      ).toBe('true');
      expect(
        decodeDhcpOptionValue(
          DhcpOptionValueTypeEnum.Boolean,
          encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Boolean, 'false'),
        ),
      ).toBe('false');
    });
  });

  describe('integers', () => {
    it('round-trips UInt8/UInt16/UInt32 at their boundaries', () => {
      expect(
        decodeDhcpOptionValue(
          DhcpOptionValueTypeEnum.UInt8,
          encodeDhcpOptionValue(DhcpOptionValueTypeEnum.UInt8, '255'),
        ),
      ).toBe('255');
      expect(
        decodeDhcpOptionValue(
          DhcpOptionValueTypeEnum.UInt16,
          encodeDhcpOptionValue(DhcpOptionValueTypeEnum.UInt16, '65535'),
        ),
      ).toBe('65535');
      expect(
        decodeDhcpOptionValue(
          DhcpOptionValueTypeEnum.UInt32,
          encodeDhcpOptionValue(DhcpOptionValueTypeEnum.UInt32, '4294967295'),
        ),
      ).toBe('4294967295');
    });

    it('rejects an out-of-range UInt8', () => {
      expect(() => encodeDhcpOptionValue(DhcpOptionValueTypeEnum.UInt8, '256')).toThrow();
    });

    it('round-trips a Duration as plain seconds', () => {
      const hex = encodeDhcpOptionValue(DhcpOptionValueTypeEnum.Duration, '86400');
      expect(decodeDhcpOptionValue(DhcpOptionValueTypeEnum.Duration, hex)).toBe('86400');
    });
  });

  describe('String', () => {
    it('round-trips ASCII text', () => {
      const hex = encodeDhcpOptionValue(DhcpOptionValueTypeEnum.String, 'example.lan');
      expect(decodeDhcpOptionValue(DhcpOptionValueTypeEnum.String, hex)).toBe('example.lan');
    });
  });

  describe('OpaqueHex / uncataloged (null valueType)', () => {
    it('passes a valid hex string through, canonicalized to uppercase', () => {
      expect(encodeDhcpOptionValue(DhcpOptionValueTypeEnum.OpaqueHex, 'c0a8')).toBe('C0A8');
      expect(encodeDhcpOptionValue(null, 'c0a8')).toBe('C0A8');
    });

    it('rejects an odd-length hex string', () => {
      expect(() => encodeDhcpOptionValue(DhcpOptionValueTypeEnum.OpaqueHex, 'C0A')).toThrow();
    });

    it('rejects non-hex characters', () => {
      expect(() => encodeDhcpOptionValue(DhcpOptionValueTypeEnum.OpaqueHex, 'ZZ')).toThrow();
    });
  });
});
