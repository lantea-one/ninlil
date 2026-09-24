import { DnsResourceRecordTypeEnum } from '../../../../api-client';
import { RECORD_TYPE_FORM_REGISTRY, SUPPORTED_RECORD_TYPES } from './record-type-form.registry';

describe('RECORD_TYPE_FORM_REGISTRY', () => {
  it('has exactly one registered form component for every type SUPPORTED_RECORD_TYPES names', () => {
    for (const type of SUPPORTED_RECORD_TYPES) {
      expect(RECORD_TYPE_FORM_REGISTRY[type]).withContext(type).toBeDefined();
    }
  });

  it('registers no type outside of SUPPORTED_RECORD_TYPES (the two stay in sync)', () => {
    expect(Object.keys(RECORD_TYPE_FORM_REGISTRY).sort()).toEqual(
      [...SUPPORTED_RECORD_TYPES].sort(),
    );
  });

  it('names exactly one entry per type SUPPORTED_RECORD_TYPES lists, with no duplicates', () => {
    expect(new Set(SUPPORTED_RECORD_TYPES).size).toBe(SUPPORTED_RECORD_TYPES.length);
  });

  it(
    'covers every one of the 37 record types this project has a real backend codec for -- ' +
      'confirmed directly against DnsRecordDataCodecRegistry.cs, not assumed',
    () => {
      expect(SUPPORTED_RECORD_TYPES.length).toBe(37);
      expect(SUPPORTED_RECORD_TYPES).toEqual(
        jasmine.arrayContaining([
          // The 10 most-common types.
          DnsResourceRecordTypeEnum.A,
          DnsResourceRecordTypeEnum.Aaaa,
          DnsResourceRecordTypeEnum.Cname,
          DnsResourceRecordTypeEnum.Mx,
          DnsResourceRecordTypeEnum.Txt,
          DnsResourceRecordTypeEnum.Ns,
          DnsResourceRecordTypeEnum.Soa,
          DnsResourceRecordTypeEnum.Srv,
          DnsResourceRecordTypeEnum.Caa,
          DnsResourceRecordTypeEnum.Ptr,
          // The remaining, less-common types.
          DnsResourceRecordTypeEnum.Dname,
          DnsResourceRecordTypeEnum.Svcb,
          DnsResourceRecordTypeEnum.Https,
          DnsResourceRecordTypeEnum.Naptr,
          DnsResourceRecordTypeEnum.Loc,
          DnsResourceRecordTypeEnum.Uri,
          DnsResourceRecordTypeEnum.Hinfo,
          DnsResourceRecordTypeEnum.Rp,
          DnsResourceRecordTypeEnum.Kx,
          DnsResourceRecordTypeEnum.Afsdb,
          DnsResourceRecordTypeEnum.Apl,
          DnsResourceRecordTypeEnum.Eui48,
          DnsResourceRecordTypeEnum.Eui64,
          DnsResourceRecordTypeEnum.Sshfp,
          DnsResourceRecordTypeEnum.Tlsa,
          DnsResourceRecordTypeEnum.Smimea,
          DnsResourceRecordTypeEnum.Openpgpkey,
          DnsResourceRecordTypeEnum.Ipseckey,
          DnsResourceRecordTypeEnum.Cert,
          DnsResourceRecordTypeEnum.Ds,
          DnsResourceRecordTypeEnum.Cds,
          DnsResourceRecordTypeEnum.Dnskey,
          DnsResourceRecordTypeEnum.Cdnskey,
          DnsResourceRecordTypeEnum.Rrsig,
          DnsResourceRecordTypeEnum.Nsec,
          DnsResourceRecordTypeEnum.Nsec3,
          DnsResourceRecordTypeEnum.Nsec3Param,
        ]),
      );
    },
  );

  it(
    "maps both members of each of the registry's 4 shared-codec record type pairs onto the " +
      "exact same form component, mirroring the backend's own shared-codec pattern",
    () => {
      expect(RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Svcb]).toBe(
        RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Https],
      );
      expect(RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Tlsa]).toBe(
        RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Smimea],
      );
      expect(RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Ds]).toBe(
        RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Cds],
      );
      expect(RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Dnskey]).toBe(
        RECORD_TYPE_FORM_REGISTRY[DnsResourceRecordTypeEnum.Cdnskey],
      );
    },
  );
});
