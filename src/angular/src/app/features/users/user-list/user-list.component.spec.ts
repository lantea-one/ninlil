import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { UsersService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { UserListComponent } from './user-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('UserListComponent', () => {
  let usersApiSpy: {
    apiUsersGet: jasmine.Spy;
    apiUsersIdPut: jasmine.Spy;
    apiUsersIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    usersApiSpy = jasmine.createSpyObj('UsersService', [
      'apiUsersGet',
      'apiUsersIdPut',
      'apiUsersIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    usersApiSpy.apiUsersGet.and.returnValue(
      of({
        items: [
          {
            id: 'user-1',
            userName: 'jsmith',
            email: 'jsmith@example.com',
            displayName: 'J Smith',
            isDisabled: false,
            twoFactorEnabled: false,
            roles: ['dns-operator'],
            directPermissions: [],
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [UserListComponent],
      providers: [
        provideRouter([]),
        { provide: UsersService, useValue: usersApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(UserListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of user accounts on init', async () => {
    const fixture = await createComponent();

    expect(usersApiSpy.apiUsersGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.users().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(usersApiSpy.apiUsersGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('toggleDisabled() PUTs the full profile with the flipped flag, then reloads the page', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersIdPut.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.users()[0]);

    expect(usersApiSpy.apiUsersIdPut).toHaveBeenCalledWith({
      id: 'user-1',
      updateUserRequest: { displayName: 'J Smith', email: 'jsmith@example.com', isDisabled: true },
    });
    expect(usersApiSpy.apiUsersGet).toHaveBeenCalledTimes(2);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteUser() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteUser(
      fixture.componentInstance.users()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(usersApiSpy.apiUsersIdDelete).not.toHaveBeenCalled();
  });

  it('deleteUser() deletes and reloads the page once confirmed', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteUser(
      fixture.componentInstance.users()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(usersApiSpy.apiUsersIdDelete).toHaveBeenCalledWith({ id: 'user-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(usersApiSpy.apiUsersGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteUser(
      fixture.componentInstance.users()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it('resetPassword() opens the reset-password dialog for the selected account', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.resetPassword(fixture.componentInstance.users()[0]);

    expect(fixture.componentInstance.resetPasswordDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.resetPasswordDialogTarget()).toEqual({
      id: 'user-1',
      userName: 'jsmith',
      email: 'jsmith@example.com',
    });
  });
});
