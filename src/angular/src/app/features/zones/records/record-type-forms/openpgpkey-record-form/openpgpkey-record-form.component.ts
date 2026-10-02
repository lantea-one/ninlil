import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/**
 * This interface mirrors the backend's `OpenPgpKeyRecordData` field for field. `keyData` is
 * base64, the RFC 7929 presentation format and the API encoding alike.
 */
export interface OpenPgpKeyRecordFormData {
  keyData: string;
}

/**
 * This component is the `OPENPGPKEY` record data form (RFC 7929): an OpenPGP public key for the
 * owner name, which is built from a hash of the mail address's local part. The data is the
 * unframed key packet sequence, so the form is one base64 field, e.g. the output of
 * `gpg --export | base64`.
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
