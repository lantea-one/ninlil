import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToMac, MAC48_PATTERN, macToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors the backend's `Eui48RecordData` field for field; `identifier` is the
 * base64 of the six octets (see `record-data-encoding.util.ts`).
 */
export interface Eui48RecordFormData {
  identifier: string;
}

/**
 * This component is the `EUI48` record data form (RFC 7043): a 48-bit identifier, usually a MAC
 * address, for the owner name. It is edited as `00:1a:2b:3c:4d:5e` and converted with
 * {@link macToBase64} and {@link base64ToMac}.
 */
@Component({
  selector: 'app-eui48-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="eui48-identifier">Identifier (MAC address form)</label>
        <input
          cFormControl
          id="eui48-identifier"
          formControlName="identifierMac"
          placeholder="00:1a:2b:3c:4d:5e"
        />
        @if (form.controls.identifierMac.hasError('required')) {
          <div class="field-error">An identifier is required.</div>
        }
        @if (form.controls.identifierMac.hasError('pattern')) {
          <div class="field-error">Enter 6 colon-separated hex octets, e.g. 00:1a:2b:3c:4d:5e.</div>
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
export class Eui48RecordFormComponent implements RecordDataFormComponent<Eui48RecordFormData> {
  readonly initialData = input<Eui48RecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    identifierMac: ['', [Validators.required, Validators.pattern(MAC48_PATTERN)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ identifierMac: base64ToMac(data.identifier) });
    });
  }

  getData(): Eui48RecordFormData {
    return { identifier: macToBase64(this.form.getRawValue().identifierMac) };
  }
}
