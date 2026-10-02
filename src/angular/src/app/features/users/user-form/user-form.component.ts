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
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { PermissionsService, RoleSummary, RolesService, UsersService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  PermissionPickerComponent,
  PermissionToggleEvent,
} from '../../../shared/permission-picker/permission-picker.component';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * The page size requested from `GET /api/roles` for the role checkboxes. The checklist has no
 * paging, so it asks for the largest page the backend allows (500).
 */
const MAX_ROLES_PER_PAGE = 500;

/**
 * This component is the create and edit page for a user account (`/users/new` and
 * `/users/:id/edit`, `users:write`); edit mode is chosen by the `id` route parameter. It saves with
 * `POST /api/users` or `PUT /api/users/{id}`. The user name and password are create-only: the
 * update request has neither, since a sign-in name is permanent and passwords are reset from the
 * Users page.
 *
 * <p>In edit mode the page also shows role checkboxes and a {@link PermissionPickerComponent} for
 * the user's direct permissions. Each change is saved at once with `POST` or `DELETE` on
 * `/api/users/{id}/roles` or `/api/users/{id}/permissions`, since there is no endpoint to replace
 * them all.
 */
@Component({
  selector: 'app-user-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    PermissionPickerComponent,
  ],
  templateUrl: './user-form.component.html',
  styleUrl: './user-form.component.scss',
})
export class UserFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly usersApi = inject(UsersService);
  private readonly rolesApi = inject(RolesService);
  private readonly permissionsApi = inject(PermissionsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly userId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing user rather than creating a new one. */
  readonly isEditMode = this.userId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly allRoles = signal<RoleSummary[]>([]);

  /**
   * Whether {@link allRoles} couldn't be loaded (usually no `roles:read`). The role checkboxes
   * are replaced by the account's current role names and a note saying why they can't be changed.
   */
  readonly rolesUnavailable = signal(false);
  readonly assignedRoleNames = signal<string[]>([]);
  readonly togglingRoleName = signal<string | null>(null);

  readonly allPermissions = signal<string[]>([]);
  readonly grantedPermissions = signal<string[]>([]);
  readonly togglingPermission = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    userName: ['', [Validators.required]],
    password: [''],
    displayName: [''],
    email: ['', [Validators.email]],
    isDisabled: [false],
  });

  ngOnInit(): void {
    if (this.isEditMode) {
      this.form.controls.userName.disable();
      void this.loadExistingUser();
      void this.loadRolesAndPermissions();
    } else {
      this.form.controls.password.addValidators(Validators.required);
    }
  }

  /** This method creates the account (`POST`) or updates its profile (`PUT`). */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.userId) {
        await firstValueFrom(
          this.usersApi.apiUsersIdPut({
            id: this.userId,
            updateUserRequest: {
              displayName: value.displayName,
              email: value.email,
              isDisabled: value.isDisabled,
            },
          }),
        );
        this.notification.success('Account saved.');
      } else {
        await firstValueFrom(
          this.usersApi.apiUsersPost({
            createUserRequest: {
              userName: value.userName,
              password: value.password,
              email: value.email,
              displayName: value.displayName,
            },
          }),
        );
        this.notification.success('Account created.');
      }
      await this.router.navigate(['/users']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this account.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method reports whether a role's checkbox is checked. */
  isRoleChecked(roleName: string): boolean {
    return this.assignedRoleNames().includes(roleName);
  }

  /** This method builds the checkbox `id` for a role, replacing characters unsafe in an `id`. */
  roleCheckboxId(roleName: string): string {
    return `role-${roleName.replace(/[^a-zA-Z0-9]+/g, '-')}`;
  }

  /** This method adds or removes one role at once, restoring the checkbox on failure. */
  async onRoleToggled(roleName: string, event: Event): Promise<void> {
    if (!this.userId || this.togglingRoleName()) return;

    const checked = (event.target as HTMLInputElement).checked;
    const previous = this.assignedRoleNames();
    this.assignedRoleNames.set(
      checked ? [...previous, roleName] : previous.filter((name) => name !== roleName),
    );
    this.togglingRoleName.set(roleName);

    try {
      if (checked) {
        await firstValueFrom(
          this.usersApi.apiUsersIdRolesPost({
            id: this.userId,
            userRoleAssignmentRequest: { roleName },
          }),
        );
      } else {
        await firstValueFrom(
          this.usersApi.apiUsersIdRolesRoleNameDelete({ id: this.userId, roleName }),
        );
      }
      this.notification.success(
        checked ? `Granted role ${roleName}.` : `Revoked role ${roleName}.`,
      );
    } catch (error) {
      this.assignedRoleNames.set(previous);
      this.notification.error(extractErrorMessage(error, 'Could not update this role.'));
    } finally {
      this.togglingRoleName.set(null);
    }
  }

  /** This method grants or revokes one direct permission at once, restoring it on failure. */
  async onPermissionToggled(event: PermissionToggleEvent): Promise<void> {
    if (!this.userId || this.togglingPermission()) return;

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
          this.usersApi.apiUsersIdPermissionsPost({
            id: this.userId,
            userPermissionRequest: { permission: event.permission },
          }),
        );
      } else {
        await firstValueFrom(
          this.usersApi.apiUsersIdPermissionsDelete({
            id: this.userId,
            userPermissionRequest: { permission: event.permission },
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

  private async loadExistingUser(): Promise<void> {
    if (!this.userId) return;

    try {
      const user = await firstValueFrom(this.usersApi.apiUsersIdGet({ id: this.userId }));

      this.form.patchValue({
        userName: user.userName ?? '',
        displayName: user.displayName ?? '',
        email: user.email ?? '',
        isDisabled: user.isDisabled ?? false,
      });
      this.assignedRoleNames.set(user.roles ?? []);
      this.grantedPermissions.set(user.directPermissions ?? []);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this account.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * This method loads the role list and the permission list separately. The role list needs
   * `roles:read`, which a user administrator may lack; then only the role checkboxes are replaced
   * by a note ({@link rolesUnavailable}) and the rest of the form still works.
   */
  private async loadRolesAndPermissions(): Promise<void> {
    await Promise.all([this.loadRoles(), this.loadPermissions()]);
  }

  private async loadRoles(): Promise<void> {
    if (!this.authService.hasPermission('roles:read')) {
      this.rolesUnavailable.set(true);
      return;
    }

    try {
      const rolesPage = await firstValueFrom(
        this.rolesApi.apiRolesGet({ take: MAX_ROLES_PER_PAGE }),
      );
      this.allRoles.set(rolesPage.items ?? []);
    } catch (error) {
      this.rolesUnavailable.set(true);
      this.notification.error(extractErrorMessage(error, 'Could not load the role list.'));
    }
  }

  private async loadPermissions(): Promise<void> {
    try {
      this.allPermissions.set(await firstValueFrom(this.permissionsApi.apiPermissionsGet()));
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not load the permission list.'));
    }
  }
}
