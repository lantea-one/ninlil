import { Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { IconDirective } from '@coreui/icons-angular';
import { DnsClientDeviceIconEnum } from '../../../api-client';
import { DEVICE_ICON_CATALOG } from '../shared/device-icon.model';
import { findDeviceIcon } from '../shared/device-icon.util';

/**
 * This component is a reactive-forms control for choosing a known client's device icon: a compact
 * grid of every `DEVICE_ICON_CATALOG` entry drawn with its own CoreUI glyph, plus an "Automatic"
 * choice meaning no explicit icon (`null`).
 *
 * <p>With no explicit choice, the backend's own read-time, vendor-based suggestion applies; when
 * the parent passes that suggestion in via `suggestedKey`, the "Automatic" tile draws the
 * suggested glyph and names it, so an operator can see exactly what leaving the choice unset
 * means -- while a chosen icon and a merely suggested one stay visibly distinct.
 */
@Component({
  selector: 'app-device-icon-picker',
  imports: [IconDirective],
  templateUrl: './device-icon-picker.component.html',
  styleUrl: './device-icon-picker.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => DeviceIconPickerComponent),
      multi: true,
    },
  ],
})
export class DeviceIconPickerComponent implements ControlValueAccessor {
  /** The vendor-based suggestion the "Automatic" tile stands for, if the backend offered one. */
  readonly suggestedKey = input<DnsClientDeviceIconEnum | null | undefined>(null);

  /** Every choosable icon, in display order. */
  readonly catalog = DEVICE_ICON_CATALOG;

  /** The currently selected key, or `null` for "Automatic". */
  readonly value = signal<DnsClientDeviceIconEnum | null>(null);

  /** Whether the bound form control is disabled. */
  readonly disabled = signal(false);

  private onChange: (value: DnsClientDeviceIconEnum | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  /** The catalog entry of the current suggestion, for the "Automatic" tile's own glyph/label. */
  suggestion() {
    return findDeviceIcon(this.suggestedKey());
  }

  /** This method selects an icon (or `null` for "Automatic") and reports it to the form. */
  select(key: DnsClientDeviceIconEnum | null): void {
    if (this.disabled()) return;
    this.value.set(key);
    this.onChange(key);
    this.onTouched();
  }

  /** `ControlValueAccessor`: receives the form control's value; `undefined` counts as "Automatic". */
  writeValue(value: DnsClientDeviceIconEnum | null | undefined): void {
    this.value.set(value ?? null);
  }

  /** `ControlValueAccessor`: records the callback a selection is reported through. */
  registerOnChange(fn: (value: DnsClientDeviceIconEnum | null) => void): void {
    this.onChange = fn;
  }

  /** `ControlValueAccessor`: records the callback a first interaction is reported through. */
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  /** `ControlValueAccessor`: mirrors the form control's disabled state onto every tile. */
  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
