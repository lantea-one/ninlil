import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.CnameRecordData` field-for-field. */
export interface CnameRecordFormData {
  target: string;
}

/** This component is the `CNAME` record data form. See `a-record-form.component.ts` for the reference shape it copies. */
@Component({
  selector: 'app-cname-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="cname-target">Target (canonical name)</label>
        <input
          cFormControl
          id="cname-target"
          formControlName="target"
          placeholder="www.example.com."
        />
        @if (form.controls.target.hasError('required')) {
          <div class="field-error">A target name is required.</div>
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
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class CnameRecordFormComponent implements RecordDataFormComponent<CnameRecordFormData> {
  readonly initialData = input<CnameRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    target: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ target: data.target });
    });
  }

  getData(): CnameRecordFormData {
    return { target: this.form.getRawValue().target };
  }
}
