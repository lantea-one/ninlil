import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.AaaaRecordData` field-for-field. */
export interface AaaaRecordFormData {
  address: string;
}

/** This component is the `AAAA` record data form. See `a-record-form.component.ts` for the reference shape it copies. */
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
    // A deliberately loose IPv6 shape check (hex groups and at least one colon) -- full RFC 4291
    // validation (compressed-zero-run placement, embedded IPv4 tails) is left to the backend's
    // own real parser, the same "don't reimplement the authoritative check client-side" stance
    // this feature takes everywhere else.
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
