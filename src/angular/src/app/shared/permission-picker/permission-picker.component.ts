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
 * This component renders every permission string this server knows about (via
 * `GET /api/permissions`, see `Controllers.PermissionsController`) as a checkbox list, grouped by
 * resource prefix for readability (26+ ungrouped rows would be hard to scan), with the currently-
 * granted subset pre-checked. Shared by both `UserFormComponent`'s own direct-permission-claim
 * section and `RoleFormComponent`'s own permission-claim section -- built once here rather than
 * duplicating the same checkbox-list markup twice.
 *
 * This component never batches a change -- every toggle emits {@link toggled} immediately, since
 * neither `UsersController`/`RolesController` has a bulk "replace the whole permission set"
 * endpoint at all, only a per-permission grant (`POST .../permissions`) and revoke
 * (`DELETE .../permissions`); the caller is responsible for actually calling the right one and,
 * on failure, reverting {@link selectedPermissions} back to its prior value.
 */
@Component({
  selector: 'app-permission-picker',
  imports: [FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective],
  templateUrl: './permission-picker.component.html',
  styleUrl: './permission-picker.component.scss',
})
export class PermissionPickerComponent {
  /** Every permission string this server knows about, in whatever order the caller supplies. */
  readonly allPermissions = input.required<readonly string[]>();

  /** The subset of {@link allPermissions} currently granted -- pre-checks the matching boxes. */
  readonly selectedPermissions = input.required<readonly string[]>();

  /** Whether every checkbox should be rendered disabled, e.g. while a toggle is in flight. */
  readonly disabled = input(false);

  /** Fires once per checkbox click, before this component's own local state changes at all. */
  readonly toggled = output<PermissionToggleEvent>();

  /** {@link allPermissions}, grouped by resource prefix for display. */
  readonly groups = computed(() => groupPermissionsByResource(this.allPermissions()));

  /** This method reports whether a given permission is currently checked. */
  isChecked(permission: string): boolean {
    return this.selectedPermissions().includes(permission);
  }

  /**
   * This method builds a DOM-safe `id` for one permission's own checkbox -- a raw permission
   * string (e.g. `"zones:read"`) contains a colon, which is not a valid `id`/`for` character pair
   * to rely on, so every non-alphanumeric character is replaced with a hyphen.
   */
  checkboxId(permission: string): string {
    return `permission-${permission.replace(/[^a-zA-Z0-9]+/g, '-')}`;
  }

  /** This method handles one checkbox's own native change event. */
  onChange(permission: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.toggled.emit({ permission, checked });
  }
}
