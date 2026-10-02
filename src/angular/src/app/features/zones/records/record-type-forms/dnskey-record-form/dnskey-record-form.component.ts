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
import { DnssecAlgorithmEnum } from '../../../../../api-client';
import { RecordDataFormComponent } from '../record-type-form.model';

/**
 * This interface mirrors the backend's `DnsKeyRecordData` field for field. `publicKey` is base64,
 * as in zone files and the API.
 */
export interface DnsKeyRecordFormData {
  isZoneKey: boolean;
  isSecureEntryPoint: boolean;
  algorithm: DnssecAlgorithmEnum;
  publicKey: string;
  isRevoked: boolean;
}

/**
 * This component is the data form for `DNSKEY` (RFC 4034 §2) and `CDNSKEY` (RFC 7344, the key a
 * child asks its parent to turn into a DS record). Both use `DnsKeyRecordData` on the backend, so
 * `RECORD_TYPE_FORM_REGISTRY` maps both types here.
 *
 * A signed zone's keys normally come from its DNSSEC Keys page (`DnssecKeyManagementService`), so
 * writing one by hand is unusual. The form is a plain editor with one field per RDATA member.
 */
@Component({
  selector: 'app-dnskey-record-form',
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
        <label cLabel for="dnskey-algorithm">Algorithm</label>
        <select cSelect id="dnskey-algorithm" formControlName="algorithm">
          @for (algorithm of dnssecAlgorithms; track algorithm) {
            <option [value]="algorithm">{{ algorithm }}</option>
          }
        </select>
      </div>
      <div class="flags">
        <c-form-check>
          <input cFormCheckInput type="checkbox" id="dnskey-zone-key" formControlName="isZoneKey" />
          <label cFormCheckLabel for="dnskey-zone-key">Zone key</label>
        </c-form-check>
        <c-form-check>
          <input
            cFormCheckInput
            type="checkbox"
            id="dnskey-sep"
            formControlName="isSecureEntryPoint"
          />
          <label cFormCheckLabel for="dnskey-sep">Secure entry point</label>
        </c-form-check>
        <c-form-check>
          <input cFormCheckInput type="checkbox" id="dnskey-revoked" formControlName="isRevoked" />
          <label cFormCheckLabel for="dnskey-revoked">Revoked</label>
        </c-form-check>
      </div>
      <div class="field full-width">
        <label cLabel for="dnskey-public-key">Public key (base64)</label>
        <textarea
          cFormControl
          id="dnskey-public-key"
          formControlName="publicKey"
          rows="4"
        ></textarea>
        @if (form.controls.publicKey.hasError('required')) {
          <div class="field-error">A public key is required.</div>
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
    .flags {
      display: flex;
      gap: var(--ninlil-space-4);
      align-items: center;
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
export class DnsKeyRecordFormComponent implements RecordDataFormComponent<DnsKeyRecordFormData> {
  readonly initialData = input<DnsKeyRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly dnssecAlgorithms = Object.values(DnssecAlgorithmEnum);

  readonly form = this.formBuilder.group({
    isZoneKey: [true],
    isSecureEntryPoint: [false],
    algorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum, [Validators.required]],
    publicKey: ['', [Validators.required]],
    isRevoked: [false],
  });

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) this.form.patchValue(data);
    });
  }

  getData(): DnsKeyRecordFormData {
    return this.form.getRawValue();
  }
}
