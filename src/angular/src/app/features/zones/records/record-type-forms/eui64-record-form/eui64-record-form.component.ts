import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToMac, MAC64_PATTERN, macToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.Eui64RecordData` field-for-field --
 * `identifier` is the base64 form of the 8 raw octets that field's own JSON representation
 * actually carries. See `eui48-record-form.component.ts` for the near-identical shape this copies.
 */
export interface Eui64RecordFormData {
  identifier: string;
}

/**
 * This component is the `EUI64` record data form (RFC 7043) -- an 8-octet identifier associated
 * with the owner name, shown and accepted in the same colon-separated hex form
 * `Eui48RecordFormComponent` uses, just with 8 groups instead of 6.
 */
@Component({
  selector: 'app-eui64-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field">
        <label cLabel for="eui64-identifier">Identifier (colon-separated hex octets)</label>
        <input
          cFormControl
          id="eui64-identifier"
          formControlName="identifierMac"
          placeholder="00:1a:2b:3c:4d:5e:6f:70"
        />
        @if (form.controls.identifierMac.hasError('required')) {
          <div class="field-error">An identifier is required.</div>
        }
        @if (form.controls.identifierMac.hasError('pattern')) {
          <div class="field-error">Enter 8 colon-separated hex octets.</div>
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
export class Eui64RecordFormComponent implements RecordDataFormComponent<Eui64RecordFormData> {
  readonly initialData = input<Eui64RecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    identifierMac: ['', [Validators.required, Validators.pattern(MAC64_PATTERN)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ identifierMac: base64ToMac(data.identifier) });
    });
  }

  getData(): Eui64RecordFormData {
    return { identifier: macToBase64(this.form.getRawValue().identifierMac) };
  }
}
