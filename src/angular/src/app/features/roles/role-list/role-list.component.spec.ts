import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { RolesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { RoleListComponent } from './role-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Roles page: loading and paging, load errors and delete with its
 * confirmation. TestBed with spy-backed `RolesService` and `NotificationService`.
 */
describe('RoleListComponent', () => {
  let rolesApiSpy: { apiRolesGet: jasmine.Spy; apiRolesIdDelete: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    rolesApiSpy = jasmine.createSpyObj('RolesService', ['apiRolesGet', 'apiRolesIdDelete']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    rolesApiSpy.apiRolesGet.and.returnValue(
      of({
        items: [
          {
            id: 'role-1',
            name: 'dns-operator',
            description: 'Manages zones.',
            permissions: ['zones:read', 'zones:write'],
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [RoleListComponent],
      providers: [
        provideRouter([]),
        { provide: RolesService, useValue: rolesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RoleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of roles on init', async () => {
    const fixture = await createComponent();

    expect(rolesApiSpy.apiRolesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.roles().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(rolesApiSpy.apiRolesGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('shows an error message when loading fails', async () => {
    rolesApiSpy = jasmine.createSpyObj('RolesService', ['apiRolesGet', 'apiRolesIdDelete']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    rolesApiSpy.apiRolesGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    await TestBed.configureTestingModule({
      imports: [RoleListComponent],
      providers: [
        provideRouter([]),
        { provide: RolesService, useValue: rolesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RoleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toContain('Could not load roles.');
  });

  it('deleteRole() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteRole(
      fixture.componentInstance.roles()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(rolesApiSpy.apiRolesIdDelete).not.toHaveBeenCalled();
  });

  it('deleteRole() deletes and reloads the page once confirmed', async () => {
    const fixture = await createComponent();
    rolesApiSpy.apiRolesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRole(
      fixture.componentInstance.roles()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(rolesApiSpy.apiRolesIdDelete).toHaveBeenCalledWith({ id: 'role-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(rolesApiSpy.apiRolesGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    rolesApiSpy.apiRolesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteRole(
      fixture.componentInstance.roles()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
