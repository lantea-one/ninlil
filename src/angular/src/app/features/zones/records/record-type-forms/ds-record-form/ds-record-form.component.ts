import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
} from '@coreui/angular';
import { DnssecAlgorithmEnum } from '../../../../../api-client';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, EVEN_LENGTH_HEX_PATTERN, hexToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.DsRecordData` field-for-field --
 * `digest` is the base64 form of the raw digest bytes that field's own JSON representation
 * actually carries.
 */
export interface DsRecordFormData {
  keyTag: number;
  algorithm: DnssecAlgorithmEnum;
  digestType: number;
  digest: string;
}

/**
 * This component is the shared data form for both the `DS` record type (RFC 4034 §5, what a
 * parent zone actually publishes) and the `CDS` record type (RFC 7344, a child zone's own copy of
 * what it wants a parent to publish) -- the two share byte-for-byte the same RDATA shape on the
 * backend (`DsRecordData`, reused by both `DsRecordDataCodec` and `CdsRecordDataCodec`), so
 * `RECORD_TYPE_FORM_REGISTRY` maps both `DnsResourceRecordTypeEnum.Ds` and `.Cds` onto this one
 * component, mirroring `TlsaRecordFormComponent`'s own identical reasoning for `TLSA`/`SMIMEA`.
 * `digest` is shown and accepted as plain hex -- the exact form a domain registrar's own "add a DS
 * record" UI already presents it in -- converted to and from the base64 the wire format needs.
 * These are, more often than not, system-managed by this project's own DNSSEC key-management
 * tooling rather than hand-typed, but the field shape is simple enough that a full editing form
 * costs nothing extra to provide.
 */
@Component({
  selector: 'app-ds-record-form',
  imports: [
    ReactiveFormsModule,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="ds-key-tag">Key tag</label>
        <input cFormControl id="ds-key-tag" type="number" formControlName="keyTag" />
        @if (form.controls.keyTag.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="ds-algorithm">Algorithm</label>
        <select cSelect id="ds-algorithm" formControlName="algorithm">
          @for (algorithm of dnssecAlgorithms; track algorithm) {
            <option [value]="algorithm">{{ algorithm }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label cLabel for="ds-digest-type">Digest type</label>
        <select cSelect id="ds-digest-type" formControlName="digestType">
          <option [value]="1">1 - SHA-1</option>
          <option [value]="2">2 - SHA-256</option>
          <option [value]="3">3 - GOST R 34.11-94</option>
          <option [value]="4">4 - SHA-384</option>
        </select>
      </div>
      <div class="field full-width">
        <label cLabel for="ds-digest">Digest (hex)</label>
        <textarea cFormControl id="ds-digest" formControlName="digestHex" rows="2"></textarea>
        @if (form.controls.digestHex.hasError('required')) {
          <div class="field-error">A digest is required.</div>
        }
        @if (form.controls.digestHex.hasError('pattern')) {
          <div class="field-error">Enter an even number of hexadecimal digits.</div>
        }
      </div>
    </form>
  `,
  styles: `
    .field-row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--ninlil-space-4);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--ninlil-space-1);
      min-width: 220px;
      flex: 1 1 220px;
    }
    .full-width {
      width: 100%;
    }
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class DsRecordFormComponent implements RecordDataFormComponent<DsRecordFormData> {
  readonly initialData = input<DsRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly dnssecAlgorithms = Object.values(DnssecAlgorithmEnum);

  readonly form = this.formBuilder.group({
    keyTag: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    algorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum, [Validators.required]],
    digestType: [2, [Validators.required]],
    digestHex: ['', [Validators.required, Validators.pattern(EVEN_LENGTH_HEX_PATTERN)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          keyTag: data.keyTag,
          algorithm: data.algorithm,
          digestType: data.digestType,
          digestHex: base64ToHex(data.digest),
        });
      }
    });
  }

  getData(): DsRecordFormData {
    const value = this.form.getRawValue();
    return {
      keyTag: value.keyTag,
      algorithm: value.algorithm,
      digestType: value.digestType,
      digest: hexToBase64(value.digestHex),
    };
  }
}
