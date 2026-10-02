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
 * This interface mirrors the backend's `SvcbParameter` field for field. `value` is base64; the form
 * edits it as hex, the RFC 9460 §2.4 generic presentation of a parameter value.
 */
export interface SvcbParameterFormData {
  key: number;
  value: string;
}

/** This interface mirrors the backend's `ServiceBindingRecordData` field for field. */
export interface SvcbHttpsRecordFormData {
  priority: number;
  targetName: string;
  parameters: SvcbParameterFormData[];
}

/**
 * This type names one parameter row's shape so `getRawValue()` returns
 * `{ key: number; valueHex: string }` rather than an index-signature record, which
 * `noPropertyAccessFromIndexSignature` (tsconfig.json) would not allow dot access on.
 */
type SvcbParameterGroup = FormGroup<{
  key: FormControl<number>;
  valueHex: FormControl<string>;
}>;

/**
 * This component is the data form for `SVCB` and `HTTPS` (RFC 9460). Both use
 * `ServiceBindingRecordData` on the backend, so `RECORD_TYPE_FORM_REGISTRY` maps both types here.
 * A `priority` of 0 makes an alias record that points at `targetName` with no parameters; any
 * other value orders the service records for a name. Parameters are `key`/`value` rows in a
 * `FormArray`, with values edited as hex (see `record-data-encoding.util.ts`).
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

  /** This method appends an empty parameter row. */
  addParameter(): void {
    this.parameters.push(this.buildParameterGroup(0, ''));
  }

  /** This method removes the parameter row at the given index. */
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
