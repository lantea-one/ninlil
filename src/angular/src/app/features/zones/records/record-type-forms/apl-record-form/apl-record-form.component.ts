import { Component, effect, inject, input } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import {
  ButtonDirective,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { RecordDataFormComponent } from '../record-type-form.model';
import { base64ToHex, formatCidr, parseCidr } from './apl-cidr.util';

/** This function validates that a control's value is a well-formed CIDR prefix. */
function cidrValidator(control: AbstractControl<string>): ValidationErrors | null {
  try {
    parseCidr(control.value);
    return null;
  } catch {
    return { invalidCidr: true };
  }
}

/**
 * This interface mirrors the backend's `AplItem` field for field; `addressFamilyDependentPart` is
 * the base64 of the address bytes.
 */
export interface AplItemFormData {
  addressFamily: number;
  prefixLength: number;
  negated: boolean;
  addressFamilyDependentPart: string;
}

/** This interface mirrors the backend's `AplRecordData` field for field. */
export interface AplRecordFormData {
  items: AplItemFormData[];
}

/**
 * This type names one `FormArray` row's shape so `getRawValue()` returns
 * `{ cidr: string; negated: boolean }` rather than an index-signature record, which
 * `noPropertyAccessFromIndexSignature` (tsconfig.json) would not allow dot access on.
 */
type AplItemGroup = FormGroup<{
  cidr: FormControl<string>;
  negated: FormControl<boolean>;
}>;

/**
 * This component is the `APL` record data form (RFC 3123, experimental and rarely deployed): a list
 * of address prefixes for the owner name. Each item is edited as CIDR text (`192.0.2.0/24`,
 * `2001:db8::/32`) and converted with {@link parseCidr} and {@link formatCidr}. The "negated" flag,
 * written as a leading `!` in zone files, is a separate checkbox. An item with an address family
 * other than 1 (IPv4) or 2 (IPv6) has no CIDR form: its row is read-only, shows
 * `family <n>, prefix <p>, <hex>`, and {@link getData} sends the item back unchanged. `RecordFormComponent` hosts it
 * through `RECORD_TYPE_FORM_REGISTRY`.
 */
@Component({
  selector: 'app-apl-record-form',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    IconDirective,
  ],
  template: `
    <form cForm [formGroup]="form" class="apl-record-form">
      <div formArrayName="items">
        @for (item of items.controls; track $index) {
          <div [formGroupName]="$index" class="item-row">
            <div class="field full-width">
              <label cLabel [attr.for]="'apl-cidr-' + $index">{{
                isUnknownFamily(item)
                  ? 'Prefix (unknown address family, read-only)'
                  : 'Prefix (CIDR notation)'
              }}</label>
              <input
                cFormControl
                [id]="'apl-cidr-' + $index"
                formControlName="cidr"
                placeholder="192.0.2.0/24"
              />
              @if (item.get('cidr')?.hasError('required')) {
                <div class="field-error">A prefix is required.</div>
              }
              @if (item.get('cidr')?.hasError('invalidCidr')) {
                <div class="field-error">
                  Enter a valid CIDR prefix, e.g. 192.0.2.0/24 or 2001:db8::/32.
                </div>
              }
            </div>
            <c-form-check>
              <input
                cFormCheckInput
                type="checkbox"
                [id]="'apl-negated-' + $index"
                formControlName="negated"
              />
              <label cFormCheckLabel [attr.for]="'apl-negated-' + $index"
                >Negated (excludes this prefix)</label
              >
            </c-form-check>
            <button
              cButton
              size="sm"
              variant="ghost"
              color="danger"
              type="button"
              aria-label="Remove prefix"
              (click)="removeItem($index)"
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
        (click)="addItem()"
      >
        <svg cIcon name="cilPlus" size="sm"></svg>
        Add prefix
      </button>
    </form>
  `,
  styles: `
    .item-row {
      display: flex;
      align-items: center;
      gap: var(--ninlil-space-2);
      margin-bottom: var(--ninlil-space-2);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--ninlil-space-1);
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
export class AplRecordFormComponent implements RecordDataFormComponent<AplRecordFormData> {
  readonly initialData = input<AplRecordFormData | null>(null);

  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly form = this.formBuilder.group({
    items: this.formBuilder.array<AplItemGroup>([]),
  });

  /** The rows holding an unknown-family item, mapped to the item they send back unchanged. */
  private readonly unknownFamilyItems = new WeakMap<AbstractControl, AplItemFormData>();

  get items() {
    return this.form.controls.items;
  }

  constructor() {
    effect(() => {
      const data = this.initialData();
      if (data) {
        this.items.clear();
        for (const item of data.items) {
          this.items.push(
            item.addressFamily === 1 || item.addressFamily === 2
              ? this.buildItemGroup(formatCidr(item), item.negated)
              : this.buildUnknownFamilyGroup(item),
          );
        }
      }
    });
  }

  /** This method appends a new, empty prefix row for the operator to fill in. */
  addItem(): void {
    this.items.push(this.buildItemGroup('', false));
  }

  /** This method removes the prefix row at the given index. */
  removeItem(index: number): void {
    this.items.removeAt(index);
  }

  /** This method reports whether a row holds an item of an address family the form cannot edit. */
  isUnknownFamily(row: AbstractControl): boolean {
    return this.unknownFamilyItems.has(row);
  }

  /**
   * This method builds a read-only row for an item whose address family is neither 1 nor 2. The
   * row shows the family, prefix length and bytes as text; the item itself is kept aside and sent
   * back as it was, so saving the record does not change or drop it.
   */
  private buildUnknownFamilyGroup(item: AplItemFormData) {
    const hex = base64ToHex(item.addressFamilyDependentPart);
    const group = this.buildItemGroup(
      `family ${item.addressFamily}, prefix ${item.prefixLength}, ${hex.length > 0 ? hex : 'no address bytes'}`,
      item.negated,
    );
    // A disabled control is excluded from validation, so the text is not parsed as a CIDR.
    group.disable();
    this.unknownFamilyItems.set(group, { ...item });
    return group;
  }

  private buildItemGroup(cidr: string, negated: boolean) {
    return this.formBuilder.group({
      cidr: [cidr, [Validators.required, cidrValidator]],
      negated: [negated],
    });
  }

  getData(): AplRecordFormData {
    const value = this.form.getRawValue();
    return {
      items: value.items.map((item, index) => {
        const unknownItem = this.unknownFamilyItems.get(this.items.at(index));
        if (unknownItem) {
          return { ...unknownItem };
        }
        const parts = parseCidr(item.cidr);
        return {
          addressFamily: parts.addressFamily,
          prefixLength: parts.prefixLength,
          negated: item.negated,
          addressFamilyDependentPart: parts.addressFamilyDependentPart,
        };
      }),
    };
  }
}
