import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
} from '@coreui/angular';
import { DnsResourceRecordTypeEnum, DnssecAlgorithmEnum } from '../../../../../api-client';
import { RecordDataFormComponent } from '../record-type-form.model';

/**
 * This interface mirrors the backend's `RrSigRecordData` field for field. `signature` is base64,
 * as in zone files and the API.
 */
export interface RrSigRecordFormData {
  typeCovered: DnsResourceRecordTypeEnum;
  algorithm: DnssecAlgorithmEnum;
  labels: number;
  originalTimeToLiveSeconds: number;
  signatureExpiration: number;
  signatureInception: number;
  keyTag: number;
  signerName: string;
  signature: string;
}

/**
 * This component is the `RRSIG` record data form (RFC 4034 §3): the signature over one RRset.
 * Signed zones get their RRSIGs from `ZoneSigningService` on every re-sign, so this is a plain
 * field-per-member editor. `typeCovered` offers every `DnsResourceRecordTypeEnum` value, not only
 * the types this UI has forms for, since a signature can cover any RRset. The expiration and
 * inception times are 32-bit Unix timestamps, as `RrSigRecordData` stores them, not a date picker.
 */
@Component({
  selector: 'app-rrsig-record-form',
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
        <label cLabel for="rrsig-type-covered">Type covered</label>
        <select cSelect id="rrsig-type-covered" formControlName="typeCovered">
          @for (type of recordTypes; track type) {
            <option [value]="type">{{ type }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label cLabel for="rrsig-algorithm">Algorithm</label>
        <select cSelect id="rrsig-algorithm" formControlName="algorithm">
          @for (algorithm of dnssecAlgorithms; track algorithm) {
            <option [value]="algorithm">{{ algorithm }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label cLabel for="rrsig-labels">Labels</label>
        <input cFormControl id="rrsig-labels" type="number" formControlName="labels" />
      </div>
      <div class="field">
        <label cLabel for="rrsig-original-ttl">Original TTL (seconds)</label>
        <input
          cFormControl
          id="rrsig-original-ttl"
          type="number"
          formControlName="originalTimeToLiveSeconds"
        />
      </div>
      <div class="field">
        <label cLabel for="rrsig-signature-inception">Signature inception (Unix seconds)</label>
        <input
          cFormControl
          id="rrsig-signature-inception"
          type="number"
          formControlName="signatureInception"
        />
      </div>
      <div class="field">
        <label cLabel for="rrsig-signature-expiration">Signature expiration (Unix seconds)</label>
        <input
          cFormControl
          id="rrsig-signature-expiration"
          type="number"
          formControlName="signatureExpiration"
        />
      </div>
      <div class="field">
        <label cLabel for="rrsig-key-tag">Key tag</label>
        <input cFormControl id="rrsig-key-tag" type="number" formControlName="keyTag" />
      </div>
      <div class="field">
        <label cLabel for="rrsig-signer-name">Signer name</label>
        <input
          cFormControl
          id="rrsig-signer-name"
          formControlName="signerName"
          placeholder="example.com."
        />
        @if (form.controls.signerName.hasError('required')) {
          <div class="field-error">A signer name is required.</div>
        }
      </div>
      <div class="field full-width">
        <label cLabel for="rrsig-signature">Signature (base64)</label>
        <textarea cFormControl id="rrsig-signature" formControlName="signature" rows="4"></textarea>
        @if (form.controls.signature.hasError('required')) {
          <div class="field-error">A signature is required.</div>
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
export class RrSigRecordFormComponent implements RecordDataFormComponent<RrSigRecordFormData> {
  readonly initialData = input<RrSigRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly recordTypes = Object.values(DnsResourceRecordTypeEnum);
  readonly dnssecAlgorithms = Object.values(DnssecAlgorithmEnum);

  readonly form = this.formBuilder.group({
    typeCovered: [DnsResourceRecordTypeEnum.A as DnsResourceRecordTypeEnum, [Validators.required]],
    algorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum, [Validators.required]],
    // RrSigRecordData.Labels is a single byte.
    labels: [2, [Validators.required, Validators.min(0), Validators.max(255)]],
    // RrSigRecordData stores these three as 32-bit unsigned integers (the times as Unix
    // timestamps).
    originalTimeToLiveSeconds: [
      3600,
      [Validators.required, Validators.min(0), Validators.max(4294967295)],
    ],
    signatureExpiration: [0, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
    signatureInception: [0, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
    keyTag: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    signerName: ['', [Validators.required]],
    signature: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): RrSigRecordFormData {
    return this.form.getRawValue();
  }
}
