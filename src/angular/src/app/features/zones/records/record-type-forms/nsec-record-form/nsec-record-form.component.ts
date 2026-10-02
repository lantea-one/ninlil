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
 * This component is the `NSEC` record data form (RFC 4034 §4): the next owner name in canonical
 * order and the types present at this owner, which together prove a name or type does not exist.
 * For a signed zone `DenialOfExistenceProofBuilder` generates these at query time, so hand-written
 * ones are rare and `types` is a plain comma- or space-separated text field (e.g.
 * `A, AAAA, RRSIG`) rather than a multi-select.
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
