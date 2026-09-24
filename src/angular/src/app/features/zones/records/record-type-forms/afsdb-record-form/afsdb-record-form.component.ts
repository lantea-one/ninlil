import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.AfsdbRecordData` field-for-field. */
export interface AfsdbRecordFormData {
  subtype: number;
  hostname: string;
}

/**
 * This component is the `AFSDB` record data form (RFC 1183 §1) -- the location of a server for
 * the AFS distributed file system (or a similarly-structured cell database) the owner name
 * belongs to. `subtype` names which kind of server `hostname` is, per RFC 1183 §1 (1 is an AFS
 * cell database server, 2 is a DCE authenticated name server).
 */
@Component({
  selector: 'app-afsdb-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="afsdb-subtype">Subtype</label>
        <input cFormControl id="afsdb-subtype" type="number" formControlName="subtype" />
        @if (form.controls.subtype.invalid) {
          <div class="field-error">Enter a value from 0 to 65535.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="afsdb-hostname">Server hostname</label>
        <input
          cFormControl
          id="afsdb-hostname"
          formControlName="hostname"
          placeholder="afsdb.example.com."
        />
        @if (form.controls.hostname.hasError('required')) {
          <div class="field-error">A hostname is required.</div>
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
export class AfsdbRecordFormComponent implements RecordDataFormComponent<AfsdbRecordFormData> {
  readonly initialData = input<AfsdbRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    subtype: [1, [Validators.required, Validators.min(0), Validators.max(65535)]],
    hostname: ['', [Validators.required]],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): AfsdbRecordFormData {
    return this.form.getRawValue();
  }
}
