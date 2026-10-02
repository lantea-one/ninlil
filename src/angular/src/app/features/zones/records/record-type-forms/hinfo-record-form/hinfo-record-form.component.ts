import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.HinfoRecordData` field-for-field. */
export interface HinfoRecordFormData {
  cpu: string;
  operatingSystem: string;
}

/**
 * This component is the `HINFO` record data form (RFC 1035 §3.3.2): free text naming the owner
 * host's CPU and operating system.
 */
@Component({
  selector: 'app-hinfo-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="hinfo-cpu">CPU</label>
        <input cFormControl id="hinfo-cpu" formControlName="cpu" placeholder="ARM64" />
        @if (form.controls.cpu.hasError('required')) {
          <div class="field-error">A CPU description is required.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="hinfo-operating-system">Operating system</label>
        <input
          cFormControl
          id="hinfo-operating-system"
          formControlName="operatingSystem"
          placeholder="Linux"
        />
        @if (form.controls.operatingSystem.hasError('required')) {
          <div class="field-error">An operating system description is required.</div>
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
export class HinfoRecordFormComponent implements RecordDataFormComponent<HinfoRecordFormData> {
  readonly initialData = input<HinfoRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    cpu: ['', [Validators.required]],
    operatingSystem: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): HinfoRecordFormData {
    return this.form.getRawValue();
  }
}
