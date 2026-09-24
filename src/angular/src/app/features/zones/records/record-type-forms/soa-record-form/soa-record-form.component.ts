import { Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormControlDirective, FormDirective, FormLabelDirective } from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/** This interface mirrors `Ninlil.Dns.Common.Wire.Records.SoaRecordData` field-for-field. */
export interface SoaRecordFormData {
  primaryNameServer: string;
  responsiblePersonMailbox: string;
  serial: number;
  refreshSeconds: number;
  retrySeconds: number;
  expireSeconds: number;
  minimumTtlSeconds: number;
}

/**
 * This component is the `SOA` record data form -- the record every zone has exactly one of, at
 * its apex, per RFC 1035 §3.3.13. `RecordFormComponent` forces this type (and disables the type
 * selector entirely) the very first time a caller adds a record to a brand-new zone, mirroring
 * `RecordsController.Create`'s own "a zone's very first record must be its own SOA" rule -- this
 * component itself has no awareness of that bootstrap distinction, it is exactly the same form
 * used to later edit an existing zone's own SOA via PUT/PATCH.
 */
@Component({
  selector: 'app-soa-record-form',
  imports: [ReactiveFormsModule, FormControlDirective, FormDirective, FormLabelDirective],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="soa-primary-ns">Primary name server</label>
        <input
          cFormControl
          id="soa-primary-ns"
          formControlName="primaryNameServer"
          placeholder="ns1.example.com."
        />
        @if (form.controls.primaryNameServer.hasError('required')) {
          <div class="field-error">Required.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="soa-rp-mailbox">Responsible person (mailbox form)</label>
        <input
          cFormControl
          id="soa-rp-mailbox"
          formControlName="responsiblePersonMailbox"
          placeholder="admin.example.com."
        />
        @if (form.controls.responsiblePersonMailbox.hasError('required')) {
          <div class="field-error">Required.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="soa-serial">Serial</label>
        <input cFormControl id="soa-serial" type="number" formControlName="serial" />
        @if (form.controls.serial.invalid) {
          <div class="field-error">Required, 0 or greater.</div>
        }
      </div>
      <div class="field">
        <label cLabel for="soa-refresh">Refresh (seconds)</label>
        <input cFormControl id="soa-refresh" type="number" formControlName="refreshSeconds" />
      </div>
      <div class="field">
        <label cLabel for="soa-retry">Retry (seconds)</label>
        <input cFormControl id="soa-retry" type="number" formControlName="retrySeconds" />
      </div>
      <div class="field">
        <label cLabel for="soa-expire">Expire (seconds)</label>
        <input cFormControl id="soa-expire" type="number" formControlName="expireSeconds" />
      </div>
      <div class="field">
        <label cLabel for="soa-minimum-ttl">Minimum / negative-cache TTL (seconds)</label>
        <input
          cFormControl
          id="soa-minimum-ttl"
          type="number"
          formControlName="minimumTtlSeconds"
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
export class SoaRecordFormComponent implements RecordDataFormComponent<SoaRecordFormData> {
  readonly initialData = input<SoaRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  // 4294967295 is the real backend bound for every field below: SoaRecordData declares each of
  // them a 32-bit unsigned integer, and a value outside that range is rejected once it reaches
  // the REST layer.
  private static readonly uint32Range = [
    Validators.required,
    Validators.min(0),
    Validators.max(4294967295),
  ];

  readonly form = this.formBuilder.group({
    primaryNameServer: ['', [Validators.required]],
    responsiblePersonMailbox: ['', [Validators.required]],
    serial: [1, SoaRecordFormComponent.uint32Range],
    refreshSeconds: [3600, SoaRecordFormComponent.uint32Range],
    retrySeconds: [900, SoaRecordFormComponent.uint32Range],
    expireSeconds: [1209600, SoaRecordFormComponent.uint32Range],
    minimumTtlSeconds: [3600, SoaRecordFormComponent.uint32Range],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): SoaRecordFormData {
    return this.form.getRawValue();
  }
}
