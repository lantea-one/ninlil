import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.SrvRecordData` field-for-field. */
export interface SrvRecordFormData {
  priority: number;
  weight: number;
  port: number;
  target: string;
}

/** This component is the `SRV` record data form. See `a-record-form.component.ts` for the reference shape it copies. */
@Component({
  selector: 'app-srv-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="srv-priority">Priority</label>
        <input cFormControl id="srv-priority" type="number" formControlName="priority" />
        @if (form.controls.priority.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="srv-weight">Weight</label>
        <input cFormControl id="srv-weight" type="number" formControlName="weight" />
        @if (form.controls.weight.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="srv-port">Port</label>
        <input cFormControl id="srv-port" type="number" formControlName="port" />
        @if (form.controls.port.invalid) {
          <div class="field-error">0-65535</div>
        }
      </div>
      <div class="field">
        <label cLabel for="srv-target">Target host</label>
        <input
          cFormControl
          id="srv-target"
          formControlName="target"
          placeholder="service.example.com."
        />
        @if (form.controls.target.hasError('required')) {
          <div class="field-error">A target host is required.</div>
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
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class SrvRecordFormComponent implements RecordDataFormComponent<SrvRecordFormData> {
  readonly initialData = input<SrvRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  private static readonly portRange = [
    Validators.required,
    Validators.min(0),
    Validators.max(65535),
  ];

  readonly form = this.formBuilder.group({
    priority: [0, SrvRecordFormComponent.portRange],
    weight: [0, SrvRecordFormComponent.portRange],
    port: [0, SrvRecordFormComponent.portRange],
    target: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): SrvRecordFormData {
    return this.form.getRawValue();
  }
}
