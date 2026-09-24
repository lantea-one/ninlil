import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { PermissionsService, RolesService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  PermissionPickerComponent,
  PermissionToggleEvent,
} from '../../../shared/permission-picker/permission-picker.component';

/**
 * This component is both the "create a role" and "edit a role" page -- one component for both,
 * distinguished by whether the route supplies an `id`, mirroring `TsigKeyFormComponent`'s own
 * precedent. A role's own name is only ever set at creation -- `RolesController.Update`'s own
 * `UpdateRoleRequest` carries just a `description`, so the name field is disabled in edit mode
 * rather than silently accepted and ignored.
 *
 * The permission-claim section only appears in edit mode (a role must already exist before it can
 * be granted a permission at all -- `RolesController.AddPermission` is its own separate endpoint,
 * not part of `CreateRoleRequest`). Every checkbox toggle applies immediately, via
 * `POST`/`DELETE .../permissions`, rather than being queued for a later "Save" -- there is no
 * bulk-replace endpoint to batch them into, and reflects `TsigKeyListComponent.toggleDisabled`'s
 * own established "apply this one field's own change right away" convention.
 */
@Component({
  selector: 'app-role-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    PermissionPickerComponent,
  ],
  templateUrl: './role-form.component.html',
  styleUrl: './role-form.component.scss',
})
export class RoleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly rolesApi = inject(RolesService);
  private readonly permissionsApi = inject(PermissionsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly roleId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing role rather than creating a new one. */
  readonly isEditMode = this.roleId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly allPermissions = signal<string[]>([]);
  readonly grantedPermissions = signal<string[]>([]);
  readonly togglingPermission = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    // Not required: RoleEntity.Description has no [Required]/IsRequired() constraint on the
    // backend, and neither CreateRoleRequest nor UpdateRoleRequest rejects a blank one.
    description: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) {
      // A role's own name can never change after creation (RolesController.Update's own
      // UpdateRoleRequest carries only a description) -- disabled, not merely left editable and
      // silently ignored, so an operator is never misled into thinking a typed change took
      // effect.
      this.form.controls.name.disable();
      void this.loadExistingRole();
      void this.loadAllPermissions();
    }
  }

  /** This method creates (`POST`) or updates the description of (`PUT`) the role. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.roleId) {
        await firstValueFrom(
          this.rolesApi.apiRolesIdPut({
            id: this.roleId,
            updateRoleRequest: { description: value.description },
          }),
        );
        this.notification.success('Role saved.');
      } else {
        await firstValueFrom(
          this.rolesApi.apiRolesPost({
            createRoleRequest: { name: value.name, description: value.description },
          }),
        );
        this.notification.success('Role created.');
      }
      await this.router.navigate(['/roles']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this role.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * This method grants or revokes one permission immediately, optimistically updating local
   * state and reverting it if the request fails.
   */
  async onPermissionToggled(event: PermissionToggleEvent): Promise<void> {
    if (!this.roleId || this.togglingPermission()) return;

    const previous = this.grantedPermissions();
    this.grantedPermissions.set(
      event.checked
        ? [...previous, event.permission]
        : previous.filter((permission) => permission !== event.permission),
    );
    this.togglingPermission.set(event.permission);

    try {
      if (event.checked) {
        await firstValueFrom(
          this.rolesApi.apiRolesIdPermissionsPost({
            id: this.roleId,
            rolePermissionRequest: { permission: event.permission },
          }),
        );
      } else {
        await firstValueFrom(
          this.rolesApi.apiRolesIdPermissionsDelete({
            id: this.roleId,
            rolePermissionRequest: { permission: event.permission },
          }),
        );
      }
      this.notification.success(
        event.checked ? `Granted ${event.permission}.` : `Revoked ${event.permission}.`,
      );
    } catch (error) {
      this.grantedPermissions.set(previous);
      this.notification.error(extractErrorMessage(error, 'Could not update this permission.'));
    } finally {
      this.togglingPermission.set(null);
    }
  }

  private async loadExistingRole(): Promise<void> {
    if (!this.roleId) return;

    try {
      const role = await firstValueFrom(this.rolesApi.apiRolesIdGet({ id: this.roleId }));

      this.form.patchValue({ name: role.name ?? '', description: role.description ?? '' });
      this.grantedPermissions.set(role.permissions ?? []);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this role.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadAllPermissions(): Promise<void> {
    try {
      const permissions = await firstValueFrom(this.permissionsApi.apiPermissionsGet());
      this.allPermissions.set(permissions);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not load the permission list.'));
    }
  }
}
