import { Component, effect, inject, input } from '@angular/core';
import {
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import {
  ButtonDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, EVEN_LENGTH_HEX_PATTERN, hexToBase64 } from '../record-data-encoding.util';

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.SvcbParameter` field-for-field --
 * `value` is shown and accepted as hex, matching RFC 9460 §2.4's own "generic key definition"
 * presentation form for a parameter this UI does not specifically know the shape of.
 */
export interface SvcbParameterFormData {
  key: number;
  value: string;
}

/**
 * This interface mirrors `Ninlil.Dns.Common.Wire.Records.ServiceBindingRecordData`
 * field-for-field.
 */
export interface SvcbHttpsRecordFormData {
  priority: number;
  targetName: string;
  parameters: SvcbParameterFormData[];
}

/**
 * This type alias names one connection-parameter `FormArray` row's own typed shape explicitly
 * (rather than leaving it to loose inference), so `getRawValue()` produces a genuine
 * `{ key: number; valueHex: string }` object instead of a plain index-signature record --
 * `tsconfig.json`'s own `noPropertyAccessFromIndexSignature` flag requires exactly this to allow
 * ordinary dot-notation property access on the result.
 */
type SvcbParameterGroup = FormGroup<{
  key: FormControl<number>;
  valueHex: FormControl<string>;
}>;

/**
 * This component is the shared data form for both the `SVCB` and `HTTPS` record types (RFC 9460)
 * -- both share byte-for-byte the same RDATA shape on the backend (`ServiceBindingRecordData`,
 * reused by both `SvcbRecordDataCodec` and `HttpsRecordDataCodec`, differing only in what a client
 * uses each one for), so `RECORD_TYPE_FORM_REGISTRY` maps both `DnsResourceRecordTypeEnum.Svcb`
 * and `.Https` onto this one component, mirroring `TlsaRecordFormComponent`'s/
 * `DsRecordFormComponent`'s own identical reasoning for their own shared-codec record type pairs.
 * `priority` of 0 marks this as an "alias form" record (pointing entirely at `targetName`, with no
 * parameters of its own); any other value is a genuine preference among same-name records. Each
 * connection parameter is a `key`/`value` pair, edited as a small `FormArray` of rows an operator
 * can add to or remove from -- `value` is shown as hex rather than base64, per this module's own
 * documented "opaque/generic binary -> hex" convention.
 */
@Component({
  selector: 'app-svcb-https-record-form',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    IconDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="svcb-https-record-form">
      <div class="field-row">
        <div class="field">
          <label cLabel for="svcb-priority">Priority (0 = alias form)</label>
          <input cFormControl id="svcb-priority" type="number" formControlName="priority" />
          @if (form.controls.priority.invalid) {
            <div class="field-error">0-65535</div>
          }
        </div>
        <div class="field">
          <label cLabel for="svcb-target-name">Target name (or "." for this same owner name)</label>
          <input
            cFormControl
            id="svcb-target-name"
            formControlName="targetName"
            placeholder="svc.example.com."
          />
          @if (form.controls.targetName.hasError('required')) {
            <div class="field-error">A target name is required.</div>
          }
        </div>
      </div>

      <h4>Connection parameters</h4>
      <div formArrayName="parameters">
        @for (parameter of parameters.controls; track $index) {
          <div [formGroupName]="$index" class="parameter-row">
            <div class="field">
              <label cLabel [attr.for]="'svcb-param-key-' + $index">Key</label>
              <input
                cFormControl
                [id]="'svcb-param-key-' + $index"
                type="number"
                formControlName="key"
              />
            </div>
            <div class="field full-width">
              <label cLabel [attr.for]="'svcb-param-value-' + $index">Value (hex)</label>
              <input
                cFormControl
                [id]="'svcb-param-value-' + $index"
                formControlName="valueHex"
                placeholder="0268320268312e31"
              />
            </div>
            <button
              cButton
              size="sm"
              variant="ghost"
              color="danger"
              type="button"
              aria-label="Remove parameter"
              (click)="removeParameter($index)"
            >
              <svg cIcon name="cilTrash" size="sm"></svg>
            </button>
          </div>
        }
      </div>
      <button
        cButton
        size="sm"
        variant="outline"
        color="secondary"
        type="button"
        (click)="addParameter()"
      >
        <svg cIcon name="cilPlus" size="sm"></svg>
        Add parameter
      </button>
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
    .parameter-row {
      display: flex;
      align-items: center;
      gap: var(--ninlil-space-2);
      margin-bottom: var(--ninlil-space-2);
    }
    .full-width {
      flex: 1;
    }
    .field-error {
      font-size: var(--ninlil-font-size-xs);
      color: var(--cui-danger, #e55353);
    }
  `,
})
export class SvcbHttpsRecordFormComponent implements RecordDataFormComponent<SvcbHttpsRecordFormData> {
  readonly initialData = input<SvcbHttpsRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    priority: [0, [Validators.required, Validators.min(0), Validators.max(65535)]],
    targetName: ['.', [Validators.required]],
    parameters: this.formBuilder.array<SvcbParameterGroup>([]),
  });

  get parameters() {
    return this.form.controls.parameters;
  }

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.form.patchValue({ priority: data.priority, targetName: data.targetName });
        this.parameters.clear();
        for (const parameter of data.parameters) {
          this.parameters.push(
            this.buildParameterGroup(parameter.key, base64ToHex(parameter.value)),
          );
        }
      }
    });
  }

  /** This method appends a new, empty connection-parameter row for the operator to fill in. */
  addParameter(): void {
    this.parameters.push(this.buildParameterGroup(0, ''));
  }

  /** This method removes the connection-parameter row at the given index. */
  removeParameter(index: number): void {
    this.parameters.removeAt(index);
  }

  private buildParameterGroup(key: number, valueHex: string) {
    return this.formBuilder.group({
      key: [key, [Validators.required, Validators.min(0), Validators.max(65535)]],
      valueHex: [valueHex, [Validators.pattern(EVEN_LENGTH_HEX_PATTERN)]],
    });
  }

  getData(): SvcbHttpsRecordFormData {
    const value = this.form.getRawValue();
    return {
      priority: value.priority,
      targetName: value.targetName,
      parameters: value.parameters.map((parameter) => ({
        key: parameter.key,
        value: hexToBase64(parameter.valueHex),
      })),
    };
  }
}
