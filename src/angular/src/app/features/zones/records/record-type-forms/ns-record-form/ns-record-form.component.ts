import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.NsRecordData` field-for-field. */
export interface NsRecordFormData {
  nameServer: string;
}

/** This component is the `NS` record data form; it follows the pattern of `a-record-form.component.ts`. */
@Component({
  selector: 'app-ns-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="ns-name-server">Name server</label>
        <input
          cFormControl
          id="ns-name-server"
          formControlName="nameServer"
          placeholder="ns1.example.com."
        />
        @if (form.controls.nameServer.hasError('required')) {
          <div class="field-error">A name server is required.</div>
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
export class NsRecordFormComponent implements RecordDataFormComponent<NsRecordFormData> {
  readonly initialData = input<NsRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    nameServer: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ nameServer: data.nameServer });
    });
  }

  getData(): NsRecordFormData {
    return { nameServer: this.form.getRawValue().nameServer };
  }
}
