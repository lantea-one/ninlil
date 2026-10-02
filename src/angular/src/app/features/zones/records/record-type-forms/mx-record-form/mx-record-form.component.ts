import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.MxRecordData` field-for-field. */
export interface MxRecordFormData {
  preference: number;
  exchange: string;
}

/**
 * This component is the `MX` record data form. It is the simplest multi-field example: a
 * `.field-row` of side-by-side `.field` divs, otherwise as in `a-record-form.component.ts`.
 */
@Component({
  selector: 'app-mx-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="mx-preference">Preference</label>
        <input cFormControl id="mx-preference" type="number" formControlName="preference" />
        @if (form.controls.preference.invalid) {
          <div class="field-error">Enter a value from 0 to 65535.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="mx-exchange">Mail exchange host</label>
        <input
          cFormControl
          id="mx-exchange"
          formControlName="exchange"
          placeholder="mail.example.com."
        />
        @if (form.controls.exchange.hasError('required')) {
          <div class="field-error">A mail exchange host is required.</div>
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
export class MxRecordFormComponent implements RecordDataFormComponent<MxRecordFormData> {
  readonly initialData = input<MxRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    preference: [10, [Validators.required, Validators.min(0), Validators.max(65535)]],
    exchange: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ preference: data.preference, exchange: data.exchange });
    });
  }

  getData(): MxRecordFormData {
    const value = this.form.getRawValue();
    return { preference: value.preference, exchange: value.exchange };
  }
}
