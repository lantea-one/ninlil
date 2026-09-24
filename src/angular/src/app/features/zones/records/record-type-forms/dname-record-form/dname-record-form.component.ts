import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.DnameRecordData` field-for-field. */
export interface DnameRecordFormData {
  target: string;
}

/**
 * This component is the `DNAME` record data form (RFC 6672) -- like `CnameRecordFormComponent`,
 * but redirecting an entire subtree of names rather than a single one. See
 * `a-record-form.component.ts` for the reference shape it copies.
 */
@Component({
  selector: 'app-dname-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="dname-target">Target subtree</label>
        <input
          cFormControl
          id="dname-target"
          formControlName="target"
          placeholder="new-location.example.com."
        />
        @if (form.controls.target.hasError('required')) {
          <div class="field-error">A target is required.</div>
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
export class DnameRecordFormComponent implements RecordDataFormComponent<DnameRecordFormData> {
  readonly initialData = input<DnameRecordFormData | null>(null);

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

  getData(): DnameRecordFormData {
    return { target: this.form.getRawValue().target };
  }
}
