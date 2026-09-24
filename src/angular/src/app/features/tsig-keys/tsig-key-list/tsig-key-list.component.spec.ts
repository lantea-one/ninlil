import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TsigKeysService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { TsigKeyListComponent } from './tsig-key-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('TsigKeyListComponent', () => {
  // A manually-shaped spy interface, not `jasmine.SpyObj<TsigKeysService>` -- see
  // `zone-list.component.spec.ts`'s own identical note for why.
  let tsigKeysApiSpy: {
    apiTsigKeysGet: jasmine.Spy;
    apiTsigKeysIdPatch: jasmine.Spy;
    apiTsigKeysIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    tsigKeysApiSpy = jasmine.createSpyObj('TsigKeysService', [
      'apiTsigKeysGet',
      'apiTsigKeysIdPatch',
      'apiTsigKeysIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
      of({
        items: [
          { id: 'key-1', name: 'secondary1', algorithmName: 'hmac-sha256', isDisabled: false },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [TsigKeyListComponent],
      providers: [
        provideRouter([]),
        { provide: TsigKeysService, useValue: tsigKeysApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(TsigKeyListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of keys on init', async () => {
    const fixture = await createComponent();

    expect(tsigKeysApiSpy.apiTsigKeysGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.keys().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(tsigKeysApiSpy.apiTsigKeysGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    tsigKeysApiSpy.apiTsigKeysIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.keys()[0]);

    expect(tsigKeysApiSpy.apiTsigKeysIdPatch).toHaveBeenCalledWith({
      id: 'key-1',
      body: { isDisabled: true },
    });
    expect(tsigKeysApiSpy.apiTsigKeysGet).toHaveBeenCalledTimes(2);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteKey() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(tsigKeysApiSpy.apiTsigKeysIdDelete).not.toHaveBeenCalled();
  });

  it('deleteKey() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    tsigKeysApiSpy.apiTsigKeysIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(tsigKeysApiSpy.apiTsigKeysIdDelete).toHaveBeenCalledWith({ id: 'key-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(tsigKeysApiSpy.apiTsigKeysGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    tsigKeysApiSpy.apiTsigKeysIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
