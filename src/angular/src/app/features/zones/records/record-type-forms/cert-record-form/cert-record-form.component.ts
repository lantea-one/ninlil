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

/** A sentinel form-control value standing in for "no algorithm used with the certificate". */
const NO_ALGORITHM = 'NONE';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.CertRecordData` field-for-field --
 * `algorithm` is `null` for RFC 4398 §2.1's own "no algorithm used" zero value, and `certificate`
 * is base64, matching that field's own real-world PEM-without-headers presentation convention and
 * the wire format's own default `byte[]` JSON encoding, so no conversion is needed here.
 */
export interface CertRecordFormData {
  certificateType: number;
  keyTag: number;
  algorithm: DnssecAlgorithmEnum | null;
  certificate: string;
}

/**
 * This component is the `CERT` record data form (RFC 4398) -- a certificate or certificate
 * revocation list published for the owner name. `certificateType` is a narrow, IANA-registered
 * numeric value (1 is PKIX/X.509, 3 is PGP, and so on) kept as a plain number rather than a
 * select, since the full "Certificate Type Values" registry is large and this project has no
 * shared enum for it; `algorithm` reuses the same `DnssecAlgorithmEnum` a `DNSKEY`/`DS` record
 * does, plus an explicit "(none)" option for RFC 4398 §2.1's own zero value.
 */
@Component({
  selector: 'app-cert-record-form',
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
        <label cLabel for="cert-certificate-type">Certificate type</label>
        <input
          cFormControl
          id="cert-certificate-type"
          type="number"
          formControlName="certificateType"
        />
        @if (form.controls.certificateType.invalid) {
          <div class="field-error">0-65535 (1 is PKIX/X.509, 3 is PGP -- see IANA's registry)</div>
        }
      </div>
      <div class="field">
        <label cLabel for="cert-key-tag">Key tag</label>
        <input cFormControl id="cert-key-tag" type="number" formControlName="keyTag" />
        @if (form.controls.keyTag.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="cert-algorithm">Algorithm</label>
        <select cSelect id="cert-algorithm" formControlName="algorithm">
          <option [value]="noAlgorithmValue">(none)</option>
          @for (algorithm of dnssecAlgorithms; track algorithm) {
            <option [value]="algorithm">{{ algorithm }}</option>
          }
        </select>
      </div>
      <div class="field full-width">
        <label cLabel for="cert-certificate">Certificate (base64)</label>
        <textarea
          cFormControl
          id="cert-certificate"
          formControlName="certificate"
          rows="4"
        ></textarea>
        @if (form.controls.certificate.hasError('required')) {
          <div class="field-error">A certificate is required.</div>
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
export class CertRecordFormComponent implements RecordDataFormComponent<CertRecordFormData> {
  readonly initialData = input<CertRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly dnssecAlgorithms = Object.values(DnssecAlgorithmEnum);
  readonly noAlgorithmValue = NO_ALGORITHM;

  readonly form = this.formBuilder.group({
    certificateType: [1, [Validators.required, Validators.min(0), Validators.max(65535)]],
    keyTag: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    algorithm: [NO_ALGORITHM as DnssecAlgorithmEnum | typeof NO_ALGORITHM],
    certificate: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          certificateType: data.certificateType,
          keyTag: data.keyTag,
          algorithm: data.algorithm ?? NO_ALGORITHM,
          certificate: data.certificate,
        });
      }
    });
  }

  getData(): CertRecordFormData {
    const value = this.form.getRawValue();
    return {
      certificateType: value.certificateType,
      keyTag: value.keyTag,
      algorithm: value.algorithm === NO_ALGORITHM ? null : value.algorithm,
      certificate: value.certificate,
    };
  }
}
