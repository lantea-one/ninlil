import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ClientGroupsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientGroupListComponent } from './client-group-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the client group list: loading, the enable/disable toggle, "make default"
 * and delete with their confirmations. TestBed with a spy-backed `ClientGroupsService` and
 * `NotificationService`.
 */
describe('ClientGroupListComponent', () => {
  let clientGroupsApiSpy: {
    apiClientGroupsGet: jasmine.Spy;
    apiClientGroupsIdPatch: jasmine.Spy;
    apiClientGroupsIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', [
      'apiClientGroupsGet',
      'apiClientGroupsIdPatch',
      'apiClientGroupsIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientGroupsApiSpy.apiClientGroupsGet.and.returnValue(
      of({
        items: [{ id: 'group-1', name: 'lan', isDefault: false, isDisabled: false }],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientGroupListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientGroupListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of client groups on init', async () => {
    const fixture = await createComponent();

    expect(clientGroupsApiSpy.apiClientGroupsGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.groups().length).toBe(1);
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    clientGroupsApiSpy.apiClientGroupsIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.groups()[0]);

    expect(clientGroupsApiSpy.apiClientGroupsIdPatch).toHaveBeenCalledWith({
      id: 'group-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('makeDefault() does nothing when the group is already the default', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.groups.set([
      { id: 'group-1', name: 'lan', isDefault: true, isDisabled: false },
    ]);

    await fixture.componentInstance.makeDefault(fixture.componentInstance.groups()[0]);

    expect(fixture.componentInstance.confirmDialogVisible()).toBeFalse();
  });

  it('makeDefault() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const makeDefaultPromise = fixture.componentInstance.makeDefault(
      fixture.componentInstance.groups()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await makeDefaultPromise;

    expect(clientGroupsApiSpy.apiClientGroupsIdPatch).not.toHaveBeenCalled();
  });

  it('makeDefault() sends an isDefault merge patch once confirmed', async () => {
    const fixture = await createComponent();
    clientGroupsApiSpy.apiClientGroupsIdPatch.and.returnValue(of(undefined));

    const makeDefaultPromise = fixture.componentInstance.makeDefault(
      fixture.componentInstance.groups()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await makeDefaultPromise;

    expect(clientGroupsApiSpy.apiClientGroupsIdPatch).toHaveBeenCalledWith({
      id: 'group-1',
      body: { isDefault: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteGroup() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    clientGroupsApiSpy.apiClientGroupsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteGroup(
      fixture.componentInstance.groups()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(clientGroupsApiSpy.apiClientGroupsIdDelete).toHaveBeenCalledWith({ id: 'group-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    clientGroupsApiSpy.apiClientGroupsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteGroup(
      fixture.componentInstance.groups()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
