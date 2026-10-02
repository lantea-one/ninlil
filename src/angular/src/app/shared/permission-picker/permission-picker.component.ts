import { Component, computed, input, output } from '@angular/core';
import {
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
} from '@coreui/angular';
import { groupPermissionsByResource } from './permission-grouping.util';

/** This interface describes one checkbox toggle {@link PermissionPickerComponent} emits. */
export interface PermissionToggleEvent {
  /** The permission string the checkbox controls. */
  permission: string;

  /** Whether the box was just checked (`true`, grant) or unchecked (`false`, revoke). */
  checked: boolean;
}

/**
 * This component shows permissions as checkboxes grouped by resource, with the granted ones
 * checked. `UserFormComponent` uses it for a user's direct permissions and `RoleFormComponent`
 * for a role's permissions; both load the list from `GET /api/permissions`.
 *
 * <p>Each click emits {@link toggled} at once. The users and roles APIs grant and revoke one
 * permission at a time (`POST` and `DELETE /api/users/{id}/permissions`, and the same under
 * `/api/roles`), so the caller makes that call and updates {@link selectedPermissions}, or
 * restores it if the call fails.
 */
@Component({
  selector: 'app-permission-picker',
  imports: [FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective],
  templateUrl: './permission-picker.component.html',
  styleUrl: './permission-picker.component.scss',
})
export class PermissionPickerComponent {
  /** Every permission the server defines. */
  readonly allPermissions = input.required<readonly string[]>();

  /** The granted subset of {@link allPermissions}; their boxes are checked. */
  readonly selectedPermissions = input.required<readonly string[]>();

  /** Whether every checkbox is disabled, e.g. while a toggle is in flight. */
  readonly disabled = input(false);

  /** Emits once per checkbox click. The checked state comes from {@link selectedPermissions}. */
  readonly toggled = output<PermissionToggleEvent>();

  /** {@link allPermissions}, grouped by resource prefix for display. */
  readonly groups = computed(() => groupPermissionsByResource(this.allPermissions()));

  /** This method reports whether a given permission is currently checked. */
  isChecked(permission: string): boolean {
    return this.selectedPermissions().includes(permission);
  }

  /**
   * This method builds the checkbox `id` for a permission, replacing the colon and any other
   * non-alphanumeric run with a hyphen so the `id` is also a plain CSS selector.
   */
  checkboxId(permission: string): string {
    return `permission-${permission.replace(/[^a-zA-Z0-9]+/g, '-')}`;
  }

  /** This method emits {@link toggled} for a checkbox change. */
  onChange(permission: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.toggled.emit({ permission, checked });
  }
}
