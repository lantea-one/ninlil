import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.PtrRecordData` field-for-field. */
export interface PtrRecordFormData {
  target: string;
}

/**
 * This component is the `PTR` record data form. PTR records usually sit in an `in-addr.arpa` or
 * `ip6.arpa` reverse zone and point at a host name; `PtrSyncService` can manage them
 * automatically. The owner name is not checked to be a reverse name, since that is convention,
 * not a protocol rule.
 */
@Component({
  selector: 'app-ptr-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="ptr-target">Target (hostname)</label>
        <input
          cFormControl
          id="ptr-target"
          formControlName="target"
          placeholder="host.example.com."
        />
        @if (form.controls.target.hasError('required')) {
          <div class="field-error">A target hostname is required.</div>
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
export class PtrRecordFormComponent implements RecordDataFormComponent<PtrRecordFormData> {
  readonly initialData = input<PtrRecordFormData | null>(null);

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

  getData(): PtrRecordFormData {
    return { target: this.form.getRawValue().target };
  }
}
