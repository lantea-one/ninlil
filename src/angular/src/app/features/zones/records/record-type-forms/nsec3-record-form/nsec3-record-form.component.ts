import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
} from '@coreui/angular';
import { DnsResourceRecordTypeEnum } from '../../../../../api-client';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, EVEN_LENGTH_HEX_PATTERN, hexToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors the backend's `Nsec3RecordData` field for field. `salt` and
 * `nextHashedOwnerName` are base64; the form edits both as hex (the RFC 5155 §3.3 presentation of
 * a salt) and converts with {@link hexToBase64} and {@link base64ToHex}.
 */
export interface Nsec3RecordFormData {
  hashAlgorithm: number;
  isOptOut: boolean;
  iterations: number;
  salt: string;
  nextHashedOwnerName: string;
  types: DnsResourceRecordTypeEnum[];
}

/**
 * This component is the `NSEC3` record data form (RFC 5155 §3). Like NSEC, but the next name is a
 * salted, iterated hash, so the chain cannot be walked to list the zone. Signed zones generate
 * these, so like `NsecRecordFormComponent` it is a plain field-per-member editor.
 */
@Component({
  selector: 'app-nsec3-record-form',
  imports: [
    ReactiveFormsModule,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="nsec3-hash-algorithm">Hash algorithm</label>
        <input
          cFormControl
          id="nsec3-hash-algorithm"
          type="number"
          formControlName="hashAlgorithm"
        />
        <div class="field-hint">1 is SHA-1, the only value ever assigned.</div>
      </div>
      <div class="field">
        <label cLabel for="nsec3-iterations">Iterations</label>
        <input cFormControl id="nsec3-iterations" type="number" formControlName="iterations" />
      </div>
      <c-form-check>
        <input cFormCheckInput type="checkbox" id="nsec3-opt-out" formControlName="isOptOut" />
        <label cFormCheckLabel for="nsec3-opt-out">Opt-out (covers an insecure delegation)</label>
      </c-form-check>
      <div class="field">
        <label cLabel for="nsec3-salt">Salt (hex, blank for none)</label>
        <input cFormControl id="nsec3-salt" formControlName="saltHex" />
        @if (form.controls.saltHex.hasError('pattern')) {
          <div class="field-error">Enter an even number of hexadecimal digits.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="nsec3-next-hashed-owner-name">Next hashed owner name (hex)</label>
        <input
          cFormControl
          id="nsec3-next-hashed-owner-name"
          formControlName="nextHashedOwnerNameHex"
        />
        @if (form.controls.nextHashedOwnerNameHex.hasError('required')) {
          <div class="field-error">A next hashed owner name is required.</div>
        }
        @if (form.controls.nextHashedOwnerNameHex.hasError('pattern')) {
          <div class="field-error">Enter an even number of hexadecimal digits.</div>
        }
      </div>
      <div class="field full-width">
        <label cLabel for="nsec3-types"
          >Types present at the original name (comma or space separated)</label
        >
        <input
          cFormControl
          id="nsec3-types"
          formControlName="typesText"
          placeholder="A, AAAA, RRSIG"
        />
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
    .field-hint {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-secondary-color, #8a93a2);
    }
  `,
})
export class Nsec3RecordFormComponent implements RecordDataFormComponent<Nsec3RecordFormData> {
  readonly initialData = input<Nsec3RecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    hashAlgorithm: [1, [Validators.required, Validators.min(0), Validators.max(255)]],
    isOptOut: [false],
    iterations: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    saltHex: ['', [Validators.pattern(EVEN_LENGTH_HEX_PATTERN)]],
    nextHashedOwnerNameHex: [
      '',
      [Validators.required, Validators.pattern(EVEN_LENGTH_HEX_PATTERN)],
    ],
    typesText: [''],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          hashAlgorithm: data.hashAlgorithm,
          isOptOut: data.isOptOut,
          iterations: data.iterations,
          saltHex: base64ToHex(data.salt),
          nextHashedOwnerNameHex: base64ToHex(data.nextHashedOwnerName),
          typesText: data.types.join(', '),
        });
      }
    });
  }

  getData(): Nsec3RecordFormData {
    const value = this.form.getRawValue();
    const types = value.typesText
      .split(/[\s,]+/)
      .map((type) => type.trim().toUpperCase())
      .filter((type) => type.length > 0) as DnsResourceRecordTypeEnum[];

    return {
      hashAlgorithm: value.hashAlgorithm,
      isOptOut: value.isOptOut,
      iterations: value.iterations,
      salt: hexToBase64(value.saltHex),
      nextHashedOwnerName: hexToBase64(value.nextHashedOwnerNameHex),
      types,
    };
  }
}
