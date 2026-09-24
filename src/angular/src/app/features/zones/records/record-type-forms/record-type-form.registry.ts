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
 * This constant is the pluggable record-type-to-form-component registry every new record-type
 * data-entry form registers itself in, and the reason {@link RecordFormComponent} never needs a
 * giant conditional template: given a {@link DnsResourceRecordTypeEnum}, this map names the one
 * standalone component (conforming to {@link RecordDataFormComponent}) responsible for that
 * type's own data fields. The registry is complete: every one of the 37 record types
 * `Ninlil.Dns.Common.Wire.Records.DnsRecordDataCodecRegistry` registers a real codec for (the
 * source of truth this registry was built directly against, not guessed at) has an entry here.
 *
 * **A single form component can back more than one enum key**: several backend record types
 * share byte-for-byte the same RDATA shape and the same `IDnsRecordDataCodec`-derived data type
 * on the backend (`ServiceBindingRecordData` for `Svcb`/`Https`; `TlsaRecordData` for
 * `Tlsa`/`Smimea`; `DsRecordData` for `Ds`/`Cds`; `DnsKeyRecordData` for `Dnskey`/`Cdnskey`) --
 * see each backend model's own doc comment for why. Rather than hand-writing 4 more
 * near-duplicate form components that would only ever differ in their own selector/class name,
 * this registry simply maps each of those 8 enum keys onto the *same* component instance type,
 * the exact frontend analogue of the backend's own shared-codec pattern. `RecordDataFormComponent`'s
 * own contract needed zero changes to support this -- `RECORD_TYPE_FORM_REGISTRY`'s value type is
 * `Type<RecordDataFormComponent>`, and nothing prevents two keys from naming the same `Type`.
 *
 * **One genuinely rare, "experimental" record type (`Apl`, RFC 3123) still fits the existing
 * contract cleanly** -- its own `AplItem.AddressFamilyDependentPart` is a trimmed-to-significant-
 * bytes `byte[]`, technically just another base64 field, but shown here as plain CIDR notation via
 * a small, dedicated `apl-cidr.util.ts` conversion helper instead, since a raw byte array would
 * be unusable to hand-author. No record type actually requires a deviation from
 * {@link RecordDataFormComponent} itself -- see `record-data-encoding.util.ts` for the
 * hex-vs-base64 convention every other `byte[]`-backed field follows.
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
 * This constant lists every record type {@link RECORD_TYPE_FORM_REGISTRY} currently supports, in
 * a fixed, deliberately curated display order (not alphabetical) matching how an operator most
 * often encounters these types in practice -- used to populate the record-type dropdown in
 * {@link RecordFormComponent}. The 10 most-common types (A/AAAA/CNAME/MX/TXT/NS/SOA/SRV/CAA/PTR)
 * lead the list; the remaining types follow, themselves grouped by rough real-world frequency
 * (general-purpose redirection/service-discovery/legacy types, then DANE/TLS-adjacent types, then
 * the genuinely DNSSEC-internal types an operator only rarely, if ever, hand-authors, last).
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
