import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { DnsResourceRecordTypeEnum } from '../../../../../api-client';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.NsecRecordData` field-for-field. */
export interface NsecRecordFormData {
  nextDomainName: string;
  types: DnsResourceRecordTypeEnum[];
}

/**
 * This component is the `NSEC` record data form (RFC 4034 §4) -- proof that a name does not exist
 * (or exists but lacks a queried type), naming the next owner name in the zone's own canonical
 * order plus every record type genuinely present at this record's own owner name. Like
 * `DnsKeyRecordFormComponent`/`RrSigRecordFormComponent`, this is a genuinely DNSSEC-internal
 * record type `Server.Authoritative`'s own `DenialOfExistenceProofBuilder` normally generates at
 * query time, so `types` is deliberately a plain, comma/space-separated text field an operator
 * types record-type names into (e.g. `A, AAAA, RRSIG`) rather than a multi-select of every
 * registered type, matching this feature's own "read-heavy/informational is fine for a
 * system-managed type" guidance.
 */
@Component({
  selector: 'app-nsec-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="nsec-next-domain-name">Next domain name</label>
        <input
          cFormControl
          id="nsec-next-domain-name"
          formControlName="nextDomainName"
          placeholder="next.example.com."
        />
        @if (form.controls.nextDomainName.hasError('required')) {
          <div class="field-error">A next domain name is required.</div>
        }
      </div>
      <div class="field full-width">
        <label cLabel for="nsec-types">Types present at this name (comma or space separated)</label>
        <input
          cFormControl
          id="nsec-types"
          formControlName="typesText"
          placeholder="A, AAAA, RRSIG"
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
    .full-width {
      width: 100%;
    }
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class NsecRecordFormComponent implements RecordDataFormComponent<NsecRecordFormData> {
  readonly initialData = input<NsecRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    nextDomainName: ['', [Validators.required]],
    typesText: [''],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          nextDomainName: data.nextDomainName,
          typesText: data.types.join(', '),
        });
      }
    });
  }

  getData(): NsecRecordFormData {
    const value = this.form.getRawValue();
    const types = value.typesText
      .split(/[\s,]+/)
      .map((type) => type.trim().toUpperCase())
      .filter((type) => type.length > 0) as DnsResourceRecordTypeEnum[];

    return { nextDomainName: value.nextDomainName, types };
  }
}
