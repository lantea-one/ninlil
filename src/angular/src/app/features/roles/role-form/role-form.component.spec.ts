import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PermissionsService, RolesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { RoleFormComponent } from './role-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the role form: create and edit (name locked), saving, validation and granting
 * or revoking permissions with the rollback on failure. TestBed with spy-backed roles and
 * permissions APIs and a stubbed route.
 */
describe('RoleFormComponent', () => {
  let rolesApiSpy: {
    apiRolesIdGet: jasmine.Spy;
    apiRolesPost: jasmine.Spy;
    apiRolesIdPut: jasmine.Spy;
    apiRolesIdPermissionsPost: jasmine.Spy;
    apiRolesIdPermissionsDelete: jasmine.Spy;
  };
  let permissionsApiSpy: { apiPermissionsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(roleId: string | null, configureApis?: () => void) {
    rolesApiSpy = jasmine.createSpyObj('RolesService', [
      'apiRolesIdGet',
      'apiRolesPost',
      'apiRolesIdPut',
      'apiRolesIdPermissionsPost',
      'apiRolesIdPermissionsDelete',
    ]);
    permissionsApiSpy = jasmine.createSpyObj('PermissionsService', ['apiPermissionsGet']);
    permissionsApiSpy.apiPermissionsGet.and.returnValue(
      of(['clientgroups:read', 'zones:read', 'zones:write']),
    );
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [RoleFormComponent],
      providers: [
        provideRouter([]),
        { provide: RolesService, useValue: rolesApiSpy },
        { provide: PermissionsService, useValue: permissionsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(roleId ? { id: roleId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(RoleFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load an existing role or the permission list', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(rolesApiSpy.apiRolesIdGet).not.toHaveBeenCalled();
    expect(permissionsApiSpy.apiPermissionsGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the role, disables the name control, and loads the permission list', async () => {
    const fixture = await createComponent('role-1', () => {
      rolesApiSpy.apiRolesIdGet.and.returnValue(
        of({
          id: 'role-1',
          name: 'dns-operator',
          description: 'Manages zones.',
          permissions: ['zones:read'],
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.form.controls.name.disabled).toBeTrue();
    expect(fixture.componentInstance.form.controls.description.value).toBe('Manages zones.');
    expect(fixture.componentInstance.grantedPermissions()).toEqual(['zones:read']);
    expect(permissionsApiSpy.apiPermissionsGet).toHaveBeenCalled();
  });

  it('save() in create mode POSTs the name/description, then navigates to the list', async () => {
    const fixture = await createComponent(null, () => {
      rolesApiSpy.apiRolesPost.and.returnValue(of({ id: 'role-1' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'dns-operator',
      description: 'Manages zones.',
    });

    await fixture.componentInstance.save();

    expect(rolesApiSpy.apiRolesPost).toHaveBeenCalledWith({
      createRoleRequest: { name: 'dns-operator', description: 'Manages zones.' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/roles']);
  });

  it('save() in edit mode PUTs only the description, then navigates to the list', async () => {
    const fixture = await createComponent('role-1', () => {
      rolesApiSpy.apiRolesIdGet.and.returnValue(
        of({ id: 'role-1', name: 'dns-operator', description: 'old', permissions: [] }),
      );
      rolesApiSpy.apiRolesIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.controls.description.setValue('new description');

    await fixture.componentInstance.save();

    expect(rolesApiSpy.apiRolesIdPut).toHaveBeenCalledWith({
      id: 'role-1',
      updateRoleRequest: { description: 'new description' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/roles']);
  });

  it('onPermissionToggled() grants a permission and keeps the optimistic update on success', async () => {
    const fixture = await createComponent('role-1', () => {
      rolesApiSpy.apiRolesIdGet.and.returnValue(
        of({ id: 'role-1', name: 'dns-operator', description: 'd', permissions: [] }),
      );
      rolesApiSpy.apiRolesIdPermissionsPost.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.onPermissionToggled({
      permission: 'zones:read',
      checked: true,
    });

    expect(rolesApiSpy.apiRolesIdPermissionsPost).toHaveBeenCalledWith({
      id: 'role-1',
      rolePermissionRequest: { permission: 'zones:read' },
    });
    expect(fixture.componentInstance.grantedPermissions()).toEqual(['zones:read']);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('onPermissionToggled() revokes a permission via DELETE', async () => {
    const fixture = await createComponent('role-1', () => {
      rolesApiSpy.apiRolesIdGet.and.returnValue(
        of({ id: 'role-1', name: 'dns-operator', description: 'd', permissions: ['zones:read'] }),
      );
      rolesApiSpy.apiRolesIdPermissionsDelete.and.returnValue(of(undefined));
    });
    await flushMicrotasks();

    await fixture.componentInstance.onPermissionToggled({
      permission: 'zones:read',
      checked: false,
    });

    expect(rolesApiSpy.apiRolesIdPermissionsDelete).toHaveBeenCalledWith({
      id: 'role-1',
      rolePermissionRequest: { permission: 'zones:read' },
    });
    expect(fixture.componentInstance.grantedPermissions()).toEqual([]);
  });

  it('onPermissionToggled() reverts the optimistic update and notifies on failure', async () => {
    const fixture = await createComponent('role-1', () => {
      rolesApiSpy.apiRolesIdGet.and.returnValue(
        of({ id: 'role-1', name: 'dns-operator', description: 'd', permissions: [] }),
      );
      rolesApiSpy.apiRolesIdPermissionsPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
    });

    await fixture.componentInstance.onPermissionToggled({
      permission: 'zones:read',
      checked: true,
    });

    expect(fixture.componentInstance.grantedPermissions()).toEqual([]);
    expect(notificationSpy.error).toHaveBeenCalled();
  });

  it('accepts a blank description (RoleEntity.Description has no backend required constraint)', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: 'dns-operator', description: '' });

    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '', description: '' });

    await fixture.componentInstance.save();

    expect(rolesApiSpy.apiRolesPost).not.toHaveBeenCalled();
  });
});
