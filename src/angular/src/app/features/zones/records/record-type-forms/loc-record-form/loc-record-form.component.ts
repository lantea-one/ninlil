import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.LocRecordData` field-for-field. */
export interface LocRecordFormData {
  latitudeDegrees: number;
  longitudeDegrees: number;
  altitudeMeters: number;
  diameterMeters: number;
  horizontalPrecisionMeters: number;
  verticalPrecisionMeters: number;
}

/**
 * This component is the `LOC` record data form (RFC 1876): the owner's physical location. Fields
 * use the units of the backend's `LocRecordData` (decimal degrees and meters); the RFC 1876 wire
 * packing is handled by `LocRecordDataCodec`.
 */
@Component({
  selector: 'app-loc-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="loc-latitude">Latitude (degrees, + is north)</label>
        <input cFormControl id="loc-latitude" type="number" formControlName="latitudeDegrees" />
        @if (form.controls.latitudeDegrees.invalid) {
          <div class="field-error">-90 to 90</div>
        }
      </div>
      <div class="field">
        <label cLabel for="loc-longitude">Longitude (degrees, + is east)</label>
        <input cFormControl id="loc-longitude" type="number" formControlName="longitudeDegrees" />
        @if (form.controls.longitudeDegrees.invalid) {
          <div class="field-error">-180 to 180</div>
        }
      </div>
      <div class="field">
        <label cLabel for="loc-altitude">Altitude (meters)</label>
        <input cFormControl id="loc-altitude" type="number" formControlName="altitudeMeters" />
      </div>
      <div class="field">
        <label cLabel for="loc-diameter">Diameter (meters)</label>
        <input cFormControl id="loc-diameter" type="number" formControlName="diameterMeters" />
      </div>
      <div class="field">
        <label cLabel for="loc-horizontal-precision">Horizontal precision (meters)</label>
        <input
          cFormControl
          id="loc-horizontal-precision"
          type="number"
          formControlName="horizontalPrecisionMeters"
        />
      </div>
      <div class="field">
        <label cLabel for="loc-vertical-precision">Vertical precision (meters)</label>
        <input
          cFormControl
          id="loc-vertical-precision"
          type="number"
          formControlName="verticalPrecisionMeters"
        />
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
export class LocRecordFormComponent implements RecordDataFormComponent<LocRecordFormData> {
  readonly initialData = input<LocRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    latitudeDegrees: [0, [Validators.required, Validators.min(-90), Validators.max(90)]],
    longitudeDegrees: [0, [Validators.required, Validators.min(-180), Validators.max(180)]],
    altitudeMeters: [0, [Validators.required]],
    diameterMeters: [1, [Validators.required, Validators.min(0)]],
    horizontalPrecisionMeters: [10000, [Validators.required, Validators.min(0)]],
    verticalPrecisionMeters: [10, [Validators.required, Validators.min(0)]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): LocRecordFormData {
    return this.form.getRawValue();
  }
}
