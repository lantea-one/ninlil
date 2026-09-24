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
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.SshfpRecordData` field-for-field --
 * `fingerprint` is the base64 form of the raw hash bytes that field's own JSON representation
 * actually carries.
 */
export interface SshfpRecordFormData {
  algorithm: number;
  fingerprintType: number;
  fingerprint: string;
}

/**
 * This component is the `SSHFP` record data form (RFC 4255) -- an SSH host key's fingerprint,
 * letting a client verify a server's key out-of-band from its first connection. `algorithm` and
 * `fingerprintType` are each a narrow, record-specific IANA registry (kept as a raw number on the
 * backend model rather than a shared enum, per `SshfpRecordData`'s own doc comment), offered here
 * as a select of every currently-assigned value. `fingerprint` is shown and accepted as plain
 * hex -- the same form `ssh-keygen -r`'s own output already uses -- converted to and from the
 * base64 the wire format needs via {@link hexToBase64}/{@link base64ToHex}.
 */
@Component({
  selector: 'app-sshfp-record-form',
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
        <label cLabel for="sshfp-algorithm">Public key algorithm</label>
        <select cSelect id="sshfp-algorithm" formControlName="algorithm">
          <option [value]="1">1 - RSA</option>
          <option [value]="2">2 - DSA</option>
          <option [value]="3">3 - ECDSA</option>
          <option [value]="4">4 - Ed25519</option>
          <option [value]="6">6 - Ed448</option>
        </select>
      </div>
      <div class="field">
        <label cLabel for="sshfp-fingerprint-type">Fingerprint hash type</label>
        <select cSelect id="sshfp-fingerprint-type" formControlName="fingerprintType">
          <option [value]="1">1 - SHA-1</option>
          <option [value]="2">2 - SHA-256</option>
        </select>
      </div>
      <div class="field full-width">
        <label cLabel for="sshfp-fingerprint">Fingerprint (hex)</label>
        <input
          cFormControl
          id="sshfp-fingerprint"
          formControlName="fingerprintHex"
          placeholder="a1b2c3..."
        />
        @if (form.controls.fingerprintHex.hasError('required')) {
          <div class="field-error">A fingerprint is required.</div>
        }
        @if (form.controls.fingerprintHex.hasError('pattern')) {
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
export class SshfpRecordFormComponent implements RecordDataFormComponent<SshfpRecordFormData> {
  readonly initialData = input<SshfpRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    algorithm: [1, [Validators.required]],
    fingerprintType: [2, [Validators.required]],
    fingerprintHex: ['', [Validators.required, Validators.pattern(EVEN_LENGTH_HEX_PATTERN)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          algorithm: data.algorithm,
          fingerprintType: data.fingerprintType,
          fingerprintHex: base64ToHex(data.fingerprint),
        });
      }
    });
  }

  getData(): SshfpRecordFormData {
    const value = this.form.getRawValue();
    return {
      algorithm: value.algorithm,
      fingerprintType: value.fingerprintType,
      fingerprint: hexToBase64(value.fingerprintHex),
    };
  }
}
