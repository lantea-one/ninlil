import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
} from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, EVEN_LENGTH_HEX_PATTERN, hexToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors the backend's `TlsaRecordData` field for field;
 * `certificateAssociationData` is the base64 of the association bytes.
 */
export interface TlsaRecordFormData {
  certificateUsage: number;
  selector: number;
  matchingType: number;
  certificateAssociationData: string;
}

/**
 * This component is the data form for `TLSA` (RFC 6698, DANE for TLS certificates) and `SMIMEA`
 * (RFC 8162, the same for S/MIME). Both use `TlsaRecordData` on the backend, so
 * `RECORD_TYPE_FORM_REGISTRY` maps both types here. The association data is edited in hex, as in
 * zone files, and converted with {@link hexToBase64} and {@link base64ToHex}.
 */
@Component({
  selector: 'app-tlsa-record-form',
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
        <label cLabel for="tlsa-usage">Certificate usage</label>
        <select cSelect id="tlsa-usage" formControlName="certificateUsage">
          <option [value]="0">0 - PKIX-TA</option>
          <option [value]="1">1 - PKIX-EE</option>
          <option [value]="2">2 - DANE-TA</option>
          <option [value]="3">3 - DANE-EE</option>
        </select>
      </div>
      <div class="field">
        <label cLabel for="tlsa-selector">Selector</label>
        <select cSelect id="tlsa-selector" formControlName="selector">
          <option [value]="0">0 - Full certificate</option>
          <option [value]="1">1 - Subject public key</option>
        </select>
      </div>
      <div class="field">
        <label cLabel for="tlsa-matching-type">Matching type</label>
        <select cSelect id="tlsa-matching-type" formControlName="matchingType">
          <option [value]="0">0 - Exact content</option>
          <option [value]="1">1 - SHA-256 hash</option>
          <option [value]="2">2 - SHA-512 hash</option>
        </select>
      </div>
      <div class="field full-width">
        <label cLabel for="tlsa-cad">Certificate association data (hex)</label>
        <textarea
          cFormControl
          id="tlsa-cad"
          formControlName="certificateAssociationDataHex"
          rows="3"
          placeholder="0123abcd..."
        ></textarea>
        @if (form.controls.certificateAssociationDataHex.hasError('required')) {
          <div class="field-error">Certificate association data is required.</div>
        }
        @if (form.controls.certificateAssociationDataHex.hasError('pattern')) {
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
export class TlsaRecordFormComponent implements RecordDataFormComponent<TlsaRecordFormData> {
  readonly initialData = input<TlsaRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    certificateUsage: [3, [Validators.required]],
    selector: [0, [Validators.required]],
    matchingType: [1, [Validators.required]],
    certificateAssociationDataHex: [
      '',
      [Validators.required, Validators.pattern(EVEN_LENGTH_HEX_PATTERN)],
    ],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          certificateUsage: data.certificateUsage,
          selector: data.selector,
          matchingType: data.matchingType,
          certificateAssociationDataHex: base64ToHex(data.certificateAssociationData),
        });
      }
    });
  }

  getData(): TlsaRecordFormData {
    const value = this.form.getRawValue();
    return {
      certificateUsage: value.certificateUsage,
      selector: value.selector,
      matchingType: value.matchingType,
      certificateAssociationData: hexToBase64(value.certificateAssociationDataHex),
    };
  }
}
