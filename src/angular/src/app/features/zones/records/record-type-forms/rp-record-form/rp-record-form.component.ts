import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.RpRecordData` field-for-field. */
export interface RpRecordFormData {
  mailbox: string;
  textReference: string;
}

/**
 * This component is the `RP` record data form (RFC 1183 §2): who is responsible for the owner
 * name. `mailbox` is written as a domain name whose first label is the local part
 * (`admin.example.com` for `admin@example.com`). `textReference` names a TXT record with more
 * information, or the root name `.` when there is none.
 */
@Component({
  selector: 'app-rp-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="rp-mailbox">Mailbox (domain-name form)</label>
        <input
          cFormControl
          id="rp-mailbox"
          formControlName="mailbox"
          placeholder="admin.example.com."
        />
        @if (form.controls.mailbox.hasError('required')) {
          <div class="field-error">A mailbox is required.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="rp-text-reference">Text reference (or root, .)</label>
        <input
          cFormControl
          id="rp-text-reference"
          formControlName="textReference"
          placeholder="."
        />
        @if (form.controls.textReference.hasError('required')) {
          <div class="field-error">A text reference is required (use "." for none).</div>
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
  `,
})
export class RpRecordFormComponent implements RecordDataFormComponent<RpRecordFormData> {
  readonly initialData = input<RpRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    mailbox: ['', [Validators.required]],
    textReference: ['.', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): RpRecordFormData {
    return this.form.getRawValue();
  }
}
