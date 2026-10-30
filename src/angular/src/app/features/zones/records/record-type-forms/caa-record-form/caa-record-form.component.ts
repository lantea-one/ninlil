import { Component, computed, effect, inject, input, signal } from '@angular/core';
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
 * This constant lists the property tags of the IANA "Certification Authority Restriction
 * Properties" registry that are in use: `issue`, `issuewild` and `iodef` (RFC 8659 §4.2 to §4.4),
 * `issuemail` (RFC 9495), `issuevmc` (Verified Mark Certificates), `contactemail` and
 * `contactphone` (CA/Browser Forum). The registry's reserved tags (`auth`, `path`, `policy`)
 * are left out. {@link CaaRecordFormComponent} offers these in its tag select.
 */
export const CAA_PROPERTY_TAGS: readonly string[] = [
  'issue',
  'issuewild',
  'iodef',
  'issuemail',
  'issuevmc',
  'contactemail',
  'contactphone',
];

/**
 * This component is the `CAA` record data form (RFC 8659). `tag` is a select of the registered
 * tags in {@link CAA_PROPERTY_TAGS}, since a mistyped tag is silently ignored by every
 * certificate authority. A stored record whose tag is not in that list (an unregistered or reserved
 * one) adds its tag as an extra option, so it loads and saves unchanged. With automatic CAA management on (see `ZoneFormComponent`),
 * `CaaRecordReconciliationService` maintains its own `issue` records; a hand edit to one of those
 * is reverted on the next reconciliation, while records added here are left alone.
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
          @for (tag of tagOptions(); track tag) {
            <option [value]="tag">{{ tag }}</option>
          }
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

  /** The loaded record's tag when the registry list lacks it, else null. */
  private readonly extraTag = signal<string | null>(null);

  /** The tags the select offers: the registered ones, plus a loaded tag the list lacks. */
  readonly tagOptions = computed(() => {
    const extra = this.extraTag();
    return extra === null ? CAA_PROPERTY_TAGS : [...CAA_PROPERTY_TAGS, extra];
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.extraTag.set(CAA_PROPERTY_TAGS.includes(data.tag) ? null : data.tag);
        this.form.patchValue(data);
      }
    });
  }

  getData(): CaaRecordFormData {
    return this.form.getRawValue();
  }
}
