import { Component, effect, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
} from '@coreui/angular';
import { RecordDataFormComponent } from '../record-type-form.model';

/**
 * This interface mirrors the backend's `IpSecKeyRecordData` field for field. `publicKey` is
 * base64, as in zone files and the API.
 */
export interface IpSecKeyRecordFormData {
  precedence: number;
  gatewayType: number;
  algorithm: number;
  gatewayAddress: string | null;
  gatewayName: string | null;
  publicKey: string;
}

/**
 * This component is the `IPSECKEY` record data form (RFC 4025): keying material for an IPsec
 * gateway for the owner name. `gatewayType` picks which gateway field applies: none (0), an IPv4
 * or IPv6 address (1 or 2) or a domain name (3). The form shows only that field, and
 * {@link getData} sends `null` for the other, matching the switch in `IpSecKeyRecordDataCodec`.
 */
@Component({
  selector: 'app-ipseckey-record-form',
  imports: [
    ReactiveFormsModule,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="field-row">
      <div class="field">
        <label cLabel for="ipseckey-precedence">Precedence</label>
        <input cFormControl id="ipseckey-precedence" type="number" formControlName="precedence" />
        @if (form.controls.precedence.invalid) {
          <div class="field-error">0-255</div>
        }
      </div>
      <div class="field">
        <label cLabel for="ipseckey-gateway-type">Gateway type</label>
        <select cSelect id="ipseckey-gateway-type" formControlName="gatewayType">
          <option [value]="0">0 - No gateway</option>
          <option [value]="1">1 - IPv4 address</option>
          <option [value]="2">2 - IPv6 address</option>
          <option [value]="3">3 - Domain name</option>
        </select>
      </div>
      <div class="field">
        <label cLabel for="ipseckey-algorithm">Public key algorithm</label>
        <select cSelect id="ipseckey-algorithm" formControlName="algorithm">
          <option [value]="0">0 - No key</option>
          <option [value]="1">1 - DSA</option>
          <option [value]="2">2 - RSA</option>
        </select>
      </div>

      @if (showGatewayAddress()) {
        <div class="field">
          <label cLabel for="ipseckey-gateway-address">Gateway address</label>
          <input
            cFormControl
            id="ipseckey-gateway-address"
            formControlName="gatewayAddressText"
            placeholder="203.0.113.1"
          />
        </div>
      }
      @if (showGatewayName()) {
        <div class="field">
          <label cLabel for="ipseckey-gateway-name">Gateway name</label>
          <input
            cFormControl
            id="ipseckey-gateway-name"
            formControlName="gatewayNameText"
            placeholder="gw.example.com."
          />
        </div>
      }

      <div class="field full-width">
        <label cLabel for="ipseckey-public-key">
          Public key (base64, empty when algorithm is "No key")
        </label>
        <textarea
          cFormControl
          id="ipseckey-public-key"
          formControlName="publicKey"
          rows="3"
        ></textarea>
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
export class IpSecKeyRecordFormComponent implements RecordDataFormComponent<IpSecKeyRecordFormData> {
  readonly initialData = input<IpSecKeyRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    precedence: [10, [Validators.required, Validators.min(0), Validators.max(255)]],
    gatewayType: [0, [Validators.required]],
    algorithm: [0, [Validators.required]],
    gatewayAddressText: [''],
    gatewayNameText: [''],
    publicKey: [''],
  });

  /** The selected gateway type, mirrored into a signal since `valueChanges` is not one. */
  private readonly gatewayType = signal(0);

  readonly showGatewayAddress = signal(false);
  readonly showGatewayName = signal(false);

  constructor() {
    this.form.controls.gatewayType.valueChanges.subscribe((type) => this.gatewayType.set(type));

    effect(() => {
      const type = this.gatewayType();
      this.showGatewayAddress.set(type === 1 || type === 2);
      this.showGatewayName.set(type === 3);
    });

    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({
          precedence: data.precedence,
          gatewayType: data.gatewayType,
          algorithm: data.algorithm,
          gatewayAddressText: data.gatewayAddress ?? '',
          gatewayNameText: data.gatewayName ?? '',
          publicKey: data.publicKey,
        });
        this.gatewayType.set(data.gatewayType);
      }
    });
  }

  getData(): IpSecKeyRecordFormData {
    const value = this.form.getRawValue();
    const type = value.gatewayType;

    return {
      precedence: value.precedence,
      gatewayType: type,
      algorithm: value.algorithm,
      gatewayAddress: type === 1 || type === 2 ? value.gatewayAddressText : null,
      gatewayName: type === 3 ? value.gatewayNameText : null,
      publicKey: value.publicKey,
    };
  }
}
