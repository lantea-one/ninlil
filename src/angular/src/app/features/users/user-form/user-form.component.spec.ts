import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PermissionsService, RolesService, UsersService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { UserFormComponent } from './user-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('UserFormComponent', () => {
  let usersApiSpy: {
    apiUsersIdGet: jasmine.Spy;
    apiUsersPost: jasmine.Spy;
    apiUsersIdPut: jasmine.Spy;
    apiUsersIdRolesPost: jasmine.Spy;
    apiUsersIdRolesRoleNameDelete: jasmine.Spy;
    apiUsersIdPermissionsPost: jasmine.Spy;
    apiUsersIdPermissionsDelete: jasmine.Spy;
  };
  let rolesApiSpy: { apiRolesGet: jasmine.Spy };
  let permissionsApiSpy: { apiPermissionsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(userId: string | null, configureApis?: () => void) {
    usersApiSpy = jasmine.createSpyObj('UsersService', [
      'apiUsersIdGet',
      'apiUsersPost',
      'apiUsersIdPut',
      'apiUsersIdRolesPost',
      'apiUsersIdRolesRoleNameDelete',
      'apiUsersIdPermissionsPost',
      'apiUsersIdPermissionsDelete',
    ]);
    rolesApiSpy = jasmine.createSpyObj('RolesService', ['apiRolesGet']);
    rolesApiSpy.apiRolesGet.and.returnValue(
      of({
        items: [{ id: 'role-1', name: 'dns-operator', description: 'd', permissions: [] }],
        skip: 0,
        take: 500,
        totalCount: 1,
      }),
    );
    permissionsApiSpy = jasmine.createSpyObj('PermissionsService', ['apiPermissionsGet']);
    permissionsApiSpy.apiPermissionsGet.and.returnValue(of(['zones:read', 'zones:write']));
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [UserFormComponent],
      providers: [
        provideRouter([]),
        { provide: UsersService, useValue: usersApiSpy },
        { provide: RolesService, useValue: rolesApiSpy },
        { provide: PermissionsService, useValue: permissionsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(userId ? { id: userId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(UserFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: requires a password and does not load an existing account', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(usersApiSpy.apiUsersIdGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.password.hasError('required')).toBeTrue();
  });

  it('edit mode: loads the account, disables userName, and loads roles/permissions', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'jsmith@example.com',
          displayName: 'J Smith',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: ['dns-operator'],
          directPermissions: ['zones:read'],
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.form.controls.userName.disabled).toBeTrue();
    expect(fixture.componentInstance.form.controls.email.value).toBe('jsmith@example.com');
    expect(fixture.componentInstance.assignedRoleNames()).toEqual(['dns-operator']);
    expect(fixture.componentInstance.grantedPermissions()).toEqual(['zones:read']);
  });

  it('save() in create mode POSTs the new account, then navigates to the list', async () => {
    const fixture = await createComponent(null, () => {
      usersApiSpy.apiUsersPost.and.returnValue(of({ id: 'user-1' }));
    });
    fixture.componentInstance.form.patchValue({
      userName: 'jsmith',
      password: 'Password-1234!',
      email: 'jsmith@example.com',
      displayName: 'J Smith',
    });

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersPost).toHaveBeenCalledWith({
      createUserRequest: {
        userName: 'jsmith',
        password: 'Password-1234!',
        email: 'jsmith@example.com',
        displayName: 'J Smith',
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/users']);
  });

  it('save() in edit mode PUTs only displayName/email/isDisabled', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'old@example.com',
          displayName: 'Old Name',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: [],
          directPermissions: [],
        }),
      );
      usersApiSpy.apiUsersIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.patchValue({ displayName: 'New Name', isDisabled: true });

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersIdPut).toHaveBeenCalledWith({
      id: 'user-1',
      updateUserRequest: {
        displayName: 'New Name',
        email: 'old@example.com',
        isDisabled: true,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/users']);
  });

  it('onRoleToggled() grants a role and keeps the optimistic update on success', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'e',
          displayName: 'd',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: [],
          directPermissions: [],
        }),
      );
      usersApiSpy.apiUsersIdRolesPost.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.onRoleToggled('dns-operator', {
      target: { checked: true },
    } as unknown as Event);

    expect(usersApiSpy.apiUsersIdRolesPost).toHaveBeenCalledWith({
      id: 'user-1',
      userRoleAssignmentRequest: { roleName: 'dns-operator' },
    });
    expect(fixture.componentInstance.assignedRoleNames()).toEqual(['dns-operator']);
  });

  it('onRoleToggled() revokes a role via DELETE using the role name in the URL', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'e',
          displayName: 'd',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: ['dns-operator'],
          directPermissions: [],
        }),
      );
      usersApiSpy.apiUsersIdRolesRoleNameDelete.and.returnValue(of(undefined));
    });
    await flushMicrotasks();

    await fixture.componentInstance.onRoleToggled('dns-operator', {
      target: { checked: false },
    } as unknown as Event);

    expect(usersApiSpy.apiUsersIdRolesRoleNameDelete).toHaveBeenCalledWith({
      id: 'user-1',
      roleName: 'dns-operator',
    });
    expect(fixture.componentInstance.assignedRoleNames()).toEqual([]);
  });

  it('onRoleToggled() reverts the optimistic update and notifies on failure', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'e',
          displayName: 'd',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: [],
          directPermissions: [],
        }),
      );
      usersApiSpy.apiUsersIdRolesPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
    });

    await fixture.componentInstance.onRoleToggled('dns-operator', {
      target: { checked: true },
    } as unknown as Event);

    expect(fixture.componentInstance.assignedRoleNames()).toEqual([]);
    expect(notificationSpy.error).toHaveBeenCalled();
  });

  it('onPermissionToggled() grants a direct permission immediately', async () => {
    const fixture = await createComponent('user-1', () => {
      usersApiSpy.apiUsersIdGet.and.returnValue(
        of({
          id: 'user-1',
          userName: 'jsmith',
          email: 'e',
          displayName: 'd',
          isDisabled: false,
          twoFactorEnabled: false,
          roles: [],
          directPermissions: [],
        }),
      );
      usersApiSpy.apiUsersIdPermissionsPost.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.onPermissionToggled({
      permission: 'zones:read',
      checked: true,
    });

    expect(usersApiSpy.apiUsersIdPermissionsPost).toHaveBeenCalledWith({
      id: 'user-1',
      userPermissionRequest: { permission: 'zones:read' },
    });
    expect(fixture.componentInstance.grantedPermissions()).toEqual(['zones:read']);
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ userName: '', password: '' });

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersPost).not.toHaveBeenCalled();
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      usersApiSpy.apiUsersPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ status: 409, error: "A user named 'jsmith' already exists." }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({
      userName: 'jsmith',
      password: 'Password-1234!',
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe("A user named 'jsmith' already exists.");
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
