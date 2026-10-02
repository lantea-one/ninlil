import { Type } from '@angular/core';
import { DnsResourceRecordTypeEnum } from '../../../../api-client';
import { RecordDataFormComponent } from './record-type-form.model';
import { ARecordFormComponent } from './a-record-form/a-record-form.component';
import { AaaaRecordFormComponent } from './aaaa-record-form/aaaa-record-form.component';
import { CnameRecordFormComponent } from './cname-record-form/cname-record-form.component';
import { MxRecordFormComponent } from './mx-record-form/mx-record-form.component';
import { TxtRecordFormComponent } from './txt-record-form/txt-record-form.component';
import { NsRecordFormComponent } from './ns-record-form/ns-record-form.component';
import { SoaRecordFormComponent } from './soa-record-form/soa-record-form.component';
import { SrvRecordFormComponent } from './srv-record-form/srv-record-form.component';
import { CaaRecordFormComponent } from './caa-record-form/caa-record-form.component';
import { PtrRecordFormComponent } from './ptr-record-form/ptr-record-form.component';
import { DnameRecordFormComponent } from './dname-record-form/dname-record-form.component';
import { SvcbHttpsRecordFormComponent } from './svcb-https-record-form/svcb-https-record-form.component';
import { NaptrRecordFormComponent } from './naptr-record-form/naptr-record-form.component';
import { LocRecordFormComponent } from './loc-record-form/loc-record-form.component';
import { UriRecordFormComponent } from './uri-record-form/uri-record-form.component';
import { HinfoRecordFormComponent } from './hinfo-record-form/hinfo-record-form.component';
import { RpRecordFormComponent } from './rp-record-form/rp-record-form.component';
import { KxRecordFormComponent } from './kx-record-form/kx-record-form.component';
import { AfsdbRecordFormComponent } from './afsdb-record-form/afsdb-record-form.component';
import { AplRecordFormComponent } from './apl-record-form/apl-record-form.component';
import { Eui48RecordFormComponent } from './eui48-record-form/eui48-record-form.component';
import { Eui64RecordFormComponent } from './eui64-record-form/eui64-record-form.component';
import { SshfpRecordFormComponent } from './sshfp-record-form/sshfp-record-form.component';
import { TlsaRecordFormComponent } from './tlsa-record-form/tlsa-record-form.component';
import { OpenPgpKeyRecordFormComponent } from './openpgpkey-record-form/openpgpkey-record-form.component';
import { IpSecKeyRecordFormComponent } from './ipseckey-record-form/ipseckey-record-form.component';
import { CertRecordFormComponent } from './cert-record-form/cert-record-form.component';
import { DsRecordFormComponent } from './ds-record-form/ds-record-form.component';
import { DnsKeyRecordFormComponent } from './dnskey-record-form/dnskey-record-form.component';
import { RrSigRecordFormComponent } from './rrsig-record-form/rrsig-record-form.component';
import { NsecRecordFormComponent } from './nsec-record-form/nsec-record-form.component';
import { Nsec3RecordFormComponent } from './nsec3-record-form/nsec3-record-form.component';
import { Nsec3ParamRecordFormComponent } from './nsec3param-record-form/nsec3param-record-form.component';

/**
 * This constant maps each {@link DnsResourceRecordTypeEnum} to the standalone component (a
 * {@link RecordDataFormComponent}) that edits that type's data, so `RecordFormComponent` needs no
 * per-type template. It covers all 37 types that the backend's `DnsRecordDataCodecRegistry`
 * (`Ninlil.Dns.Common.Wire.Records`) has a codec for.
 *
 * Some types share one component because they share the backend data type and RDATA layout:
 * `Svcb`/`Https` (`ServiceBindingRecordData`), `Tlsa`/`Smimea` (`TlsaRecordData`), `Ds`/`Cds`
 * (`DsRecordData`) and `Dnskey`/`Cdnskey` (`DnsKeyRecordData`).
 *
 * `Apl` (RFC 3123) stores each item's address part as trimmed bytes; its form shows CIDR notation
 * instead and converts with `apl-cidr.util.ts`. Other byte fields follow the hex or base64
 * convention in `record-data-encoding.util.ts`.
 */
export const RECORD_TYPE_FORM_REGISTRY: Partial<
  Record<DnsResourceRecordTypeEnum, Type<RecordDataFormComponent>>
> = {
  [DnsResourceRecordTypeEnum.A]: ARecordFormComponent,
  [DnsResourceRecordTypeEnum.Aaaa]: AaaaRecordFormComponent,
  [DnsResourceRecordTypeEnum.Cname]: CnameRecordFormComponent,
  [DnsResourceRecordTypeEnum.Mx]: MxRecordFormComponent,
  [DnsResourceRecordTypeEnum.Txt]: TxtRecordFormComponent,
  [DnsResourceRecordTypeEnum.Ns]: NsRecordFormComponent,
  [DnsResourceRecordTypeEnum.Soa]: SoaRecordFormComponent,
  [DnsResourceRecordTypeEnum.Srv]: SrvRecordFormComponent,
  [DnsResourceRecordTypeEnum.Caa]: CaaRecordFormComponent,
  [DnsResourceRecordTypeEnum.Ptr]: PtrRecordFormComponent,
  [DnsResourceRecordTypeEnum.Dname]: DnameRecordFormComponent,
  [DnsResourceRecordTypeEnum.Svcb]: SvcbHttpsRecordFormComponent,
  [DnsResourceRecordTypeEnum.Https]: SvcbHttpsRecordFormComponent,
  [DnsResourceRecordTypeEnum.Naptr]: NaptrRecordFormComponent,
  [DnsResourceRecordTypeEnum.Loc]: LocRecordFormComponent,
  [DnsResourceRecordTypeEnum.Uri]: UriRecordFormComponent,
  [DnsResourceRecordTypeEnum.Hinfo]: HinfoRecordFormComponent,
  [DnsResourceRecordTypeEnum.Rp]: RpRecordFormComponent,
  [DnsResourceRecordTypeEnum.Kx]: KxRecordFormComponent,
  [DnsResourceRecordTypeEnum.Afsdb]: AfsdbRecordFormComponent,
  [DnsResourceRecordTypeEnum.Apl]: AplRecordFormComponent,
  [DnsResourceRecordTypeEnum.Eui48]: Eui48RecordFormComponent,
  [DnsResourceRecordTypeEnum.Eui64]: Eui64RecordFormComponent,
  [DnsResourceRecordTypeEnum.Sshfp]: SshfpRecordFormComponent,
  [DnsResourceRecordTypeEnum.Tlsa]: TlsaRecordFormComponent,
  [DnsResourceRecordTypeEnum.Smimea]: TlsaRecordFormComponent,
  [DnsResourceRecordTypeEnum.Openpgpkey]: OpenPgpKeyRecordFormComponent,
  [DnsResourceRecordTypeEnum.Ipseckey]: IpSecKeyRecordFormComponent,
  [DnsResourceRecordTypeEnum.Cert]: CertRecordFormComponent,
  [DnsResourceRecordTypeEnum.Ds]: DsRecordFormComponent,
  [DnsResourceRecordTypeEnum.Cds]: DsRecordFormComponent,
  [DnsResourceRecordTypeEnum.Dnskey]: DnsKeyRecordFormComponent,
  [DnsResourceRecordTypeEnum.Cdnskey]: DnsKeyRecordFormComponent,
  [DnsResourceRecordTypeEnum.Rrsig]: RrSigRecordFormComponent,
  [DnsResourceRecordTypeEnum.Nsec]: NsecRecordFormComponent,
  [DnsResourceRecordTypeEnum.Nsec3]: Nsec3RecordFormComponent,
  [DnsResourceRecordTypeEnum.Nsec3Param]: Nsec3ParamRecordFormComponent,
};

/**
 * This constant lists the record types in {@link RECORD_TYPE_FORM_REGISTRY} in the order the type
 * select in `RecordFormComponent` shows them. The ten most common types come first, then the
 * redirection, service-discovery and legacy types, then DANE and certificate types, and last the
 * DNSSEC types an operator rarely writes by hand.
 */
export const SUPPORTED_RECORD_TYPES: readonly DnsResourceRecordTypeEnum[] = [
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
  DnsResourceRecordTypeEnum.Dname,
  DnsResourceRecordTypeEnum.Naptr,
  DnsResourceRecordTypeEnum.Svcb,
  DnsResourceRecordTypeEnum.Https,
  DnsResourceRecordTypeEnum.Uri,
  DnsResourceRecordTypeEnum.Hinfo,
  DnsResourceRecordTypeEnum.Rp,
  DnsResourceRecordTypeEnum.Kx,
  DnsResourceRecordTypeEnum.Afsdb,
  DnsResourceRecordTypeEnum.Loc,
  DnsResourceRecordTypeEnum.Apl,
  DnsResourceRecordTypeEnum.Eui48,
  DnsResourceRecordTypeEnum.Eui64,
  DnsResourceRecordTypeEnum.Sshfp,
  DnsResourceRecordTypeEnum.Tlsa,
  DnsResourceRecordTypeEnum.Smimea,
  DnsResourceRecordTypeEnum.Ipseckey,
  DnsResourceRecordTypeEnum.Openpgpkey,
  DnsResourceRecordTypeEnum.Cert,
  DnsResourceRecordTypeEnum.Ds,
  DnsResourceRecordTypeEnum.Cds,
  DnsResourceRecordTypeEnum.Dnskey,
  DnsResourceRecordTypeEnum.Cdnskey,
  DnsResourceRecordTypeEnum.Rrsig,
  DnsResourceRecordTypeEnum.Nsec,
  DnsResourceRecordTypeEnum.Nsec3,
  DnsResourceRecordTypeEnum.Nsec3Param,
];
