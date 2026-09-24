import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, EVEN_LENGTH_HEX_PATTERN, hexToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.Nsec3ParamRecordData`
 * field-for-field -- `salt` is the base64 form of its own raw bytes, shown and accepted here as
 * hex, matching `Nsec3RecordFormComponent`'s own identical field.
 */
export interface Nsec3ParamRecordFormData {
  hashAlgorithm: number;
  iterations: number;
  salt: string;
}

/**
 * This component is the `NSEC3PARAM` record data form (RFC 5155 §4) -- published at a signed
 * zone's own apex so a resolver knows exactly how to compute a matching `NSEC3` hash itself,
 * without needing to guess the zone's own hash algorithm, iteration count, or salt. The last of
 * this feature's own genuinely DNSSEC-internal record types; see `NsecRecordFormComponent`'s own
 * doc comment for why a plain field-per-RDATA-member form is deliberately all this provides.
 */
@Component({
  selector: 'app-nsec3param-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="nsec3param-hash-algorithm">Hash algorithm</label>
        <input
          cFormControl
          id="nsec3param-hash-algorithm"
          type="number"
          formControlName="hashAlgorithm"
        />
        <div class="field-hint">1 is SHA-1, the only value ever assigned.</div>
      </div>
      <div class="field">
        <label cLabel for="nsec3param-iterations">Iterations</label>
        <input cFormControl id="nsec3param-iterations" type="number" formControlName="iterations" />
      </div>
      <div class="field">
        <label cLabel for="nsec3param-salt">Salt (hex, blank for none)</label>
        <input cFormControl id="nsec3param-salt" formControlName="saltHex" />
        @if (form.controls.saltHex.hasError('pattern')) {
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
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
    .field-hint {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-secondary-color, #8a93a2);
    }
  `,
})
export class Nsec3ParamRecordFormComponent implements RecordDataFormComponent<Nsec3ParamRecordFormData> {
  readonly initialData = input<Nsec3ParamRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    hashAlgorithm: [1, [Validators.required, Validators.min(0), Validators.max(255)]],
    iterations: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    saltHex: ['', [Validators.pattern(EVEN_LENGTH_HEX_PATTERN)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          hashAlgorithm: data.hashAlgorithm,
          iterations: data.iterations,
          saltHex: base64ToHex(data.salt),
        });
      }
    });
  }

  getData(): Nsec3ParamRecordFormData {
    const value = this.form.getRawValue();
    return {
      hashAlgorithm: value.hashAlgorithm,
      iterations: value.iterations,
      salt: hexToBase64(value.saltHex),
    };
  }
}
