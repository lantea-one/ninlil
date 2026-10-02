import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.AaaaRecordData` field-for-field. */
export interface AaaaRecordFormData {
  address: string;
}

/** This component is the `AAAA` record data form; it follows the pattern of `a-record-form.component.ts`. */
@Component({
  selector: 'app-aaaa-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="aaaa-address">IPv6 address</label>
        <input cFormControl id="aaaa-address" formControlName="address" placeholder="2001:db8::1" />
        @if (form.controls.address.hasError('required')) {
          <div class="field-error">An address is required.</div>
        }
        @if (form.controls.address.hasError('pattern')) {
          <div class="field-error">Enter a valid IPv6 address.</div>
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
export class AaaaRecordFormComponent implements RecordDataFormComponent<AaaaRecordFormData> {
  readonly initialData = input<AaaaRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    // A loose check (hex digits and colons only); the backend's parser does the full RFC 4291
    // validation. It also rejects an embedded IPv4 tail such as "::ffff:192.0.2.1".
    address: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F:]+$/)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ address: data.address });
    });
  }

  getData(): AaaaRecordFormData {
    return { address: this.form.getRawValue().address };
  }
}
