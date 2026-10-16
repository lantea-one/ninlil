import { DhcpOptionValueTypeEnum } from '../../../api-client';
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from './dhcp-option-value.util';

/** These tests cover encoding and decoding each option value type, and rejecting bad input. */
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

  describe('DomainNameList', () => {
    const type = DhcpOptionValueTypeEnum.DomainNameList;
    const encodeWith = (text: string) => encodeDhcpOptionValue(type, text);

    it('round-trips several names, one per line', () => {
      const hex = encodeWith('example.com\nlab.example.org');
      expect(hex).toBe('076578616D706C6503636F6D00' + '036C6162076578616D706C65036F726700');
      expect(decodeDhcpOptionValue(type, hex)).toBe('example.com\nlab.example.org');
    });

    it('accepts one trailing dot and trims each name', () => {
      expect(encodeWith('  example.com.  ')).toBe(encodeWith('example.com'));
    });

    it('rejects an empty list and a blank name', () => {
      expect(() => encodeWith('')).toThrow();
      expect(() => encodeWith('.')).toThrow();
    });

    it('rejects an empty label', () => {
      expect(() => encodeWith('a..b')).toThrow();
    });

    it('accepts a 63-octet label and rejects a 64-octet one', () => {
      expect(() => encodeWith(`${'a'.repeat(63)}.com`)).not.toThrow();
      expect(() => encodeWith(`${'a'.repeat(64)}.com`)).toThrow();
    });

    it('rejects characters that are not printable ASCII', () => {
      expect(() => encodeWith('exämple.com')).toThrow();
      expect(() => encodeWith('a\tb.com')).toThrow();
    });

    it('accepts a name of 255 wire octets and rejects 256', () => {
      // Three 63-octet labels (3 x 64) + a 61-octet label (62) + root (1) = 255.
      const longest = [...'abc'.split('').map((c) => c.repeat(63)), 'd'.repeat(61)].join('.');
      expect(() => encodeWith(longest)).not.toThrow();
      expect(() => encodeWith(`${longest}x`)).toThrow();
    });

    it('rejects a compression pointer and a reserved label type when decoding', () => {
      expect(() => decodeDhcpOptionValue(type, 'C00C')).toThrow();
      expect(() => decodeDhcpOptionValue(type, '4000')).toThrow();
    });

    it('rejects a label that runs past the end and a missing root label', () => {
      expect(() => decodeDhcpOptionValue(type, '0561')).toThrow();
      expect(() => decodeDhcpOptionValue(type, '03616263')).toThrow();
    });

    it('rejects a non-printable label byte, an empty value and a name over 255 octets', () => {
      expect(() =>
        decodeDhcpOptionValue(type, '0161' + '0100'.slice(0, 0) + '0101' + '00'),
      ).toThrow();
      expect(() => decodeDhcpOptionValue(type, '')).toThrow();
      const label = '3F' + '61'.repeat(63);
      expect(() => decodeDhcpOptionValue(type, label.repeat(4) + '00')).toThrow();
    });

    it('rejects bytes after the last root label when they do not form a name', () => {
      expect(() => decodeDhcpOptionValue(type, '0161' + '00' + '05')).toThrow();
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
