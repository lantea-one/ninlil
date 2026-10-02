import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.NaptrRecordData` field-for-field. */
export interface NaptrRecordFormData {
  order: number;
  preference: number;
  flags: string;
  services: string;
  regularExpression: string;
  replacement: string;
}

/**
 * This component is the `NAPTR` record data form (RFC 3403): a rewrite rule, mostly used by ENUM
 * and SIP. `flags` says how to read the result (`"S"`: look up an SRV record, `"U"`: it is a
 * URI). Either `regularExpression` rewrites the query, with `replacement` set to the root name
 * `.`, or `replacement` names the next record to look up.
 */
@Component({
  selector: 'app-naptr-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="naptr-order">Order</label>
        <input cFormControl id="naptr-order" type="number" formControlName="order" />
        @if (form.controls.order.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="naptr-preference">Preference</label>
        <input cFormControl id="naptr-preference" type="number" formControlName="preference" />
        @if (form.controls.preference.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="naptr-flags">Flags</label>
        <input cFormControl id="naptr-flags" formControlName="flags" placeholder="U" />
      </div>
      <div class="field">
        <label cLabel for="naptr-services">Services</label>
        <input cFormControl id="naptr-services" formControlName="services" placeholder="E2U+sip" />
      </div>
      <div class="field full-width">
        <label cLabel for="naptr-regular-expression">Regular expression</label>
        <input
          cFormControl
          id="naptr-regular-expression"
          formControlName="regularExpression"
          placeholder="!^.*$!sip:info@example.com!"
        />
      </div>
      <div class="field">
        <label cLabel for="naptr-replacement">Replacement (or root, .)</label>
        <input cFormControl id="naptr-replacement" formControlName="replacement" placeholder="." />
        @if (form.controls.replacement.hasError('required')) {
          <div class="field-error">A replacement is required (use "." for none).</div>
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
    .full-width {
      width: 100%;
    }
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class NaptrRecordFormComponent implements RecordDataFormComponent<NaptrRecordFormData> {
  readonly initialData = input<NaptrRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  private static readonly orderRange = [
    Validators.required,
    Validators.min(0),
    Validators.max(65535),
  ];

  readonly form = this.formBuilder.group({
    order: [100, NaptrRecordFormComponent.orderRange],
    preference: [10, NaptrRecordFormComponent.orderRange],
    flags: [''],
    services: [''],
    regularExpression: [''],
    replacement: ['.', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): NaptrRecordFormData {
    return this.form.getRawValue();
  }
}
