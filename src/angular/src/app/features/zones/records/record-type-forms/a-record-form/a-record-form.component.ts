import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.ARecordData` field-for-field. */
export interface ARecordFormData {
  address: string;
}

/**
 * This component is the `A` record data form and the simplest example of the
 * {@link RecordDataFormComponent} contract: an `initialData` input set only when editing, a
 * {@link form} built with `NonNullableFormBuilder`, and {@link getData} returning the camelCase
 * `data` JSON. Its inline template shows the usual field layout: a `.field` with `cLabel`,
 * `cFormControl` and a `.field-error` per failed validator, inside a `cForm`.
 * `RecordFormComponent` hosts it through `RECORD_TYPE_FORM_REGISTRY`.
 */
@Component({
  selector: 'app-a-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="a-address">IPv4 address</label>
        <input cFormControl id="a-address" formControlName="address" placeholder="203.0.113.5" />
        @if (form.controls.address.hasError('required')) {
          <div class="field-error">An address is required.</div>
        }
        @if (form.controls.address.hasError('pattern')) {
          <div class="field-error">Enter a valid IPv4 address.</div>
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
export class ARecordFormComponent implements RecordDataFormComponent<ARecordFormData> {
  /** The stored record data when editing; `null` when creating a record. */
  readonly initialData = input<ARecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    address: ['', [Validators.required, Validators.pattern(/^(\d{1,3}\.){3}\d{1,3}$/)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ address: data.address });
    });
  }

  getData(): ARecordFormData {
    return { address: this.form.getRawValue().address };
  }
}
