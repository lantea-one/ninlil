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
 * This interface mirrors the backend's `DsRecordData` field for field; `digest` is the base64 of
 * the digest bytes.
 */
export interface DsRecordFormData {
  keyTag: number;
  algorithm: DnssecAlgorithmEnum;
  digestType: number;
  digest: string;
}

/**
 * This component is the data form for `DS` (RFC 4034 §5, published by the parent zone) and `CDS`
 * (RFC 7344, the DS a child asks its parent to publish). Both use `DsRecordData` on the backend,
 * so `RECORD_TYPE_FORM_REGISTRY` maps both types here. The digest is edited in hex, as registrars
 * show it, and sent as base64.
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
