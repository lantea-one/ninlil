import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
} from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.CaaRecordData` field-for-field. */
export interface CaaRecordFormData {
  isCritical: boolean;
  tag: string;
  value: string;
}

/**
 * This component is the `CAA` record data form (RFC 8659). `tag` offers the 3 IANA-registered
 * values as a select (`issue`/`issuewild`/`iodef`) rather than a free-text field, since a typo
 * here silently produces a policy tag no certificate authority recognizes -- the same
 * "guide toward a valid value where the valid set is small and known" reasoning
 * `zone-form.component.ts`'s own zone-type select already applies. This is also the record type
 * `CaaRecordReconciliationService` manages automatically for a zone with `AutoManageCaaRecords`
 * enabled (see `zone-form.component.ts`) -- an operator editing a
 * feature-managed `CAA` record by hand here will simply have it overwritten on the next
 * reconciliation pass, exactly as that feature's own known-limitations note documents.
 */
@Component({
  selector: 'app-caa-record-form',
  imports: [
    ReactiveFormsModule,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="caa-tag">Tag</label>
        <select cSelect id="caa-tag" formControlName="tag">
          <option value="issue">issue</option>
          <option value="issuewild">issuewild</option>
          <option value="iodef">iodef</option>
        </select>
      </div>
      <div class="field">
        <label cLabel for="caa-value">Value</label>
        <input cFormControl id="caa-value" formControlName="value" placeholder="letsencrypt.org" />
        @if (form.controls.value.hasError('required')) {
          <div class="field-error">A value is required.</div>
        }
      </div>
      <c-form-check>
        <input cFormCheckInput type="checkbox" id="caa-critical" formControlName="isCritical" />
        <label cFormCheckLabel for="caa-critical">
          Critical (a CA that doesn't recognize this tag must refuse to issue)
        </label>
      </c-form-check>
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
export class CaaRecordFormComponent implements RecordDataFormComponent<CaaRecordFormData> {
  readonly initialData = input<CaaRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    tag: ['issue', [Validators.required]],
    value: ['', [Validators.required]],
    isCritical: [false],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): CaaRecordFormData {
    return this.form.getRawValue();
  }
}
