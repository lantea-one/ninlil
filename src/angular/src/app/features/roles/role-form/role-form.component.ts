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
 * This component is the create and edit page for a role (`/roles/new` and `/roles/:id/edit`,
 * `roles:write`); edit mode is chosen by the `id` route parameter. It saves with
 * `POST /api/roles` or `PUT /api/roles/{id}`. The name is set only at creation, since the update
 * request carries just the description, so it is disabled in edit mode.
 *
 * <p>In edit mode the page also shows the role's permissions in a {@link PermissionPickerComponent}
 * (all permissions from `GET /api/permissions`). Each checkbox is saved at once with `POST` or
 * `DELETE /api/roles/{id}/permissions`, since there is no endpoint to replace them all.
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
    // Not required: the backend accepts a blank description.
    description: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) {
      // The name cannot change after creation; disabling it avoids a typed change that would be
      // silently ignored.
      this.form.controls.name.disable();
      void this.loadExistingRole();
      void this.loadAllPermissions();
    }
  }

  /** This method creates the role (`POST`) or updates its description (`PUT`). */
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
   * This method grants or revokes one permission at once. It updates the checkboxes first and
   * restores them if the request fails.
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
