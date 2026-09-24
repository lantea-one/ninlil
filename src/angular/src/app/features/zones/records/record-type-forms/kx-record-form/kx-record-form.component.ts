import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.KxRecordData` field-for-field. */
export interface KxRecordFormData {
  preference: number;
  exchanger: string;
}

/**
 * This component is the `KX` record data form (RFC 2230) -- the host responsible for key exchange
 * on the owner name's behalf, for a protocol other than e-mail (which uses `MX` instead). See
 * `mx-record-form.component.ts` for the near-identical shape this copies.
 */
@Component({
  selector: 'app-kx-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="kx-preference">Preference</label>
        <input cFormControl id="kx-preference" type="number" formControlName="preference" />
        @if (form.controls.preference.invalid) {
          <div class="field-error">Enter a value from 0 to 65535.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="kx-exchanger">Key exchanger host</label>
        <input
          cFormControl
          id="kx-exchanger"
          formControlName="exchanger"
          placeholder="kx.example.com."
        />
        @if (form.controls.exchanger.hasError('required')) {
          <div class="field-error">A key exchanger host is required.</div>
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
export class KxRecordFormComponent implements RecordDataFormComponent<KxRecordFormData> {
  readonly initialData = input<KxRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    preference: [10, [Validators.required, Validators.min(0), Validators.max(65535)]],
    exchanger: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): KxRecordFormData {
    return this.form.getRawValue();
  }
}
