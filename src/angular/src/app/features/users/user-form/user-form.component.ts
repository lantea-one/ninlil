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

/**
 * This constant bounds the single page this component requests from `GET /api/roles`, for the
 * role-assignment section's own "every role on this server" checklist -- this checklist has no
 * paginator control of its own (unlike `RoleListComponent`'s own real page-at-a-time list), so it
 * requests the largest page the backend allows to preserve a "show every role" checklist.
 */
const MAX_ROLES_PER_PAGE = 500;

/**
 * This component is both the "create a user" and "edit a user" page -- one component for both,
 * mirroring `RoleFormComponent`'s/`TsigKeyFormComponent`'s own precedent. `userName` and
 * `password` are create-only fields (`UsersController.Update`'s own `UpdateUserRequest` has
 * neither -- a sign-in name is permanent, and a password change is deliberately not this
 * endpoint's job either, per that controller's own doc comment).
 *
 * The role-assignment and direct-permission-claim sections both only appear in edit mode (a user
 * must already exist before either can be granted at all), and both apply every toggle
 * immediately -- `UsersController` has no bulk "replace the whole role/permission set" endpoint,
 * only `POST`/`DELETE` per role name and per permission, mirroring `RoleFormComponent`'s own
 * identical reasoning for its own permission section.
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

  /** This method creates (`POST`) or updates the profile of (`PUT`) the user account. */
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

  /** This method reports whether a given role is currently checked in the role picker. */
  isRoleChecked(roleName: string): boolean {
    return this.assignedRoleNames().includes(roleName);
  }

  /** This method builds a DOM-safe `id` for one role's own checkbox. */
  roleCheckboxId(roleName: string): string {
    return `role-${roleName.replace(/[^a-zA-Z0-9]+/g, '-')}`;
  }

  /** This method grants or revokes one role immediately, reverting on failure. */
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

  /** This method grants or revokes one direct permission claim immediately, reverting on failure. */
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

  private async loadRolesAndPermissions(): Promise<void> {
    try {
      const [rolesPage, permissions] = await Promise.all([
        firstValueFrom(this.rolesApi.apiRolesGet({ take: MAX_ROLES_PER_PAGE })),
        firstValueFrom(this.permissionsApi.apiPermissionsGet()),
      ]);
      this.allRoles.set(rolesPage.items ?? []);
      this.allPermissions.set(permissions);
    } catch (error) {
      this.notification.error(
        extractErrorMessage(error, 'Could not load the role/permission lists.'),
      );
    }
  }
}
