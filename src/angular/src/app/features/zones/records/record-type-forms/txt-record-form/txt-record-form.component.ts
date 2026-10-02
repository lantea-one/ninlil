import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.TxtRecordData` field-for-field. */
export interface TxtRecordFormData {
  values: string[];
}

/**
 * This component is the `TXT` record data form. The data is a list of character-strings, edited as
 * one value per textarea line rather than a `FormArray`, since values such as SPF, DKIM or
 * verification strings are usually pasted. Lines are trimmed and blank lines dropped. Binary
 * character-strings (allowed by RFC 1035 §3.3.14) cannot be entered.
 */
@Component({
  selector: 'app-txt-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form">
      <div class="field full-width">
        <label cLabel for="txt-values">Text values (one per line)</label>
        <textarea
          cFormControl
          id="txt-values"
          formControlName="valuesText"
          rows="4"
          placeholder="v=spf1 -all"
        ></textarea>
        @if (form.controls.valuesText.hasError('required')) {
          <div class="field-error">At least one text value is required.</div>
        }
      </div>
    </form>
  `,
  styles: `
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--ninlil-space-1);
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
export class TxtRecordFormComponent implements RecordDataFormComponent<TxtRecordFormData> {
  readonly initialData = input<TxtRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    valuesText: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue({ valuesText: data.values.join('\n') });
    });
  }

  getData(): TxtRecordFormData {
    const values = this.form
      .getRawValue()
      .valuesText.split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    return { values };
  }
}
