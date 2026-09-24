import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.OpenPgpKeyRecordData`
 * field-for-field -- `keyData` is base64, matching both the wire format's own default `byte[]`
 * JSON encoding and RFC 7929's own base64 presentation-format convention for this exact field, so
 * no conversion is needed here (see `record-data-encoding.util.ts`'s own doc comment).
 */
export interface OpenPgpKeyRecordFormData {
  keyData: string;
}

/**
 * This component is the `OPENPGPKEY` record data form (RFC 7929) -- an OpenPGP public key
 * published for the owner name (which itself encodes an e-mail address's local part's own SHA-256
 * hash, not the mailbox directly). The RDATA is the raw OpenPGP Transferable Public Key packet
 * sequence with no framing of its own, so this form is a single base64 field -- an operator would
 * ordinarily produce this value with `gpg --export | base64`, matching this field's own real-world
 * presentation convention exactly, unlike the hex-shown fields elsewhere in this feature.
 */
@Component({
  selector: 'app-openpgpkey-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field full-width">
        <label cLabel for="openpgpkey-key-data">OpenPGP public key (base64)</label>
        <textarea
          cFormControl
          id="openpgpkey-key-data"
          formControlName="keyData"
          rows="4"
        ></textarea>
        @if (form.controls.keyData.hasError('required')) {
          <div class="field-error">Key data is required.</div>
        }
      </div>
    </form>
  `,
  styles: `
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
export class OpenPgpKeyRecordFormComponent implements RecordDataFormComponent<OpenPgpKeyRecordFormData> {
  readonly initialData = input<OpenPgpKeyRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    keyData: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): OpenPgpKeyRecordFormData {
    return this.form.getRawValue();
  }
}
