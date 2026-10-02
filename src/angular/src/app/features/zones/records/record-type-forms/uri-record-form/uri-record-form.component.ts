import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.UriRecordData` field-for-field. */
export interface UriRecordFormData {
  priority: number;
  weight: number;
  target: string;
}

/**
 * This component is the `URI` record data form (RFC 7553): a URI for the owner name. `target` is
 * plain text, not a domain name, so no trailing dot applies.
 */
@Component({
  selector: 'app-uri-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="uri-priority">Priority</label>
        <input cFormControl id="uri-priority" type="number" formControlName="priority" />
        @if (form.controls.priority.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="uri-weight">Weight</label>
        <input cFormControl id="uri-weight" type="number" formControlName="weight" />
        @if (form.controls.weight.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field full-width">
        <label cLabel for="uri-target">Target URI</label>
        <input
          cFormControl
          id="uri-target"
          formControlName="target"
          placeholder="https://example.com/"
        />
        @if (form.controls.target.hasError('required')) {
          <div class="field-error">A target URI is required.</div>
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
export class UriRecordFormComponent implements RecordDataFormComponent<UriRecordFormData> {
  readonly initialData = input<UriRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  private static readonly priorityRange = [Validators.min(0), Validators.max(65535)];

  readonly form = this.formBuilder.group({
    priority: [10, UriRecordFormComponent.priorityRange],
    weight: [10, UriRecordFormComponent.priorityRange],
    target: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): UriRecordFormData {
    return this.form.getRawValue();
  }
}
