import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { QueryLogService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { QueryLogListComponent } from './query-log-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

const SAMPLE_ENTRY = {
  id: 'e1',
  occurredAtUtc: '2026-08-30T12:00:00Z',
  clientAddress: '192.0.2.10',
  protocol: 'Udp',
  queriedName: 'example.com.',
  queriedType: 'A',
  responseCode: 'NOERROR',
  answerCount: 1,
};

/**
 * These tests cover the Query Log page: searching, resetting and paging, the "Clear log" action
 * with and without `querylog:write`, errors, and the client identity column and filter. TestBed
 * with spy-backed `QueryLogService`, `AuthService` and `NotificationService`.
 */
describe('QueryLogListComponent', () => {
  let apiSpy: { apiQueryLogGet: jasmine.Spy; apiQueryLogDelete: jasmine.Spy };
  let authServiceSpy: { hasPermission: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApis?: () => void) {
    apiSpy = jasmine.createSpyObj('QueryLogService', ['apiQueryLogGet', 'apiQueryLogDelete']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiQueryLogGet.and.returnValue(
      of({ items: [SAMPLE_ENTRY], skip: 0, take: 25, totalCount: 1 }),
    );
    authServiceSpy.hasPermission.and.returnValue(true);
    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [QueryLogListComponent],
      providers: [
        { provide: QueryLogService, useValue: apiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(QueryLogListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('searches on init using an empty filter set and the first page', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.entries().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('hides the clear-log action when the session lacks querylog:write', async () => {
    const fixture = await createComponent(() =>
      authServiceSpy.hasPermission.and.returnValue(false),
    );

    expect(fixture.componentInstance.canClear).toBeFalse();
  });

  it('shows the clear-log action when the session holds querylog:write', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.canClear).toBeTrue();
  });

  it('search() re-issues the search from the first page using the current filter values', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.pageIndex.set(3);
    fixture.componentInstance.filterForm.patchValue({ domain: 'example.com' });
    await fixture.componentInstance.search();

    expect(fixture.componentInstance.pageIndex()).toBe(0);
    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      domain: 'example.com',
    });
  });

  it('resetFilters() clears every field, then re-searches with no filters', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.filterForm.patchValue({ domain: 'example.com' });
    await fixture.componentInstance.resetFilters();

    expect(fixture.componentInstance.filterForm.getRawValue().domain).toBe('');
    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
  });

  it('onPageChange() re-searches at the new page/size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 2, pageSize: 10 });

    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledWith({ skip: 20, take: 10 });
  });

  it('shows an error message when the search fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLogGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad filter' })),
      );
    });

    expect(fixture.componentInstance.errorMessage()).toBe('bad filter');
  });

  it('clearLog() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const clearPromise = fixture.componentInstance.clearLog();
    fixture.componentInstance.onConfirmDialogResolved(false);
    await clearPromise;

    expect(apiSpy.apiQueryLogDelete).not.toHaveBeenCalled();
  });

  it('clearLog() deletes every entry and re-searches after confirmation', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLogDelete.and.returnValue(of({ deletedCount: 7 }));
    });

    const clearPromise = fixture.componentInstance.clearLog();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await clearPromise;

    expect(apiSpy.apiQueryLogDelete).toHaveBeenCalled();
    expect(notificationSpy.success).toHaveBeenCalledWith('Deleted 7 log entries.');
    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledTimes(2);
  });

  it('clearLog() shows an error notification when the delete fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLogDelete.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 403, error: 'forbidden' })),
      );
    });

    const clearPromise = fixture.componentInstance.clearLog();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await clearPromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('forbidden');
    expect(fixture.componentInstance.clearing()).toBeFalse();
  });

  it('labels a row with the MAC or DUID its client was resolved to', async () => {
    const fixture = await createComponent(() =>
      apiSpy.apiQueryLogGet.and.returnValue(
        of({
          items: [
            {
              ...SAMPLE_ENTRY,
              clientIdentifier: 'aa:bb:cc:dd:ee:ff',
              clientProtocolVersion: 'IPv4',
            },
            {
              ...SAMPLE_ENTRY,
              id: 'e2',
              clientIdentifier: '00:01:00:01:2c:aa',
              clientProtocolVersion: 'IPv6',
            },
          ],
          skip: 0,
          take: 25,
          totalCount: 2,
        }),
      ),
    );

    const cells: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('td.cdk-column-clientIdentifier'),
    );
    expect(cells[0].textContent).toContain('MAC');
    expect(cells[0].textContent).toContain('aa:bb:cc:dd:ee:ff');
    expect(cells[1].textContent).toContain('DUID');
  });

  it('clicking a client ID filters the log to that client', async () => {
    const fixture = await createComponent();
    apiSpy.apiQueryLogGet.calls.reset();

    await fixture.componentInstance.filterByClientIdentifier({
      ...SAMPLE_ENTRY,
      clientIdentifier: 'aa:bb:cc:dd:ee:ff',
      clientProtocolVersion: 'IPv4',
    } as never);

    expect(apiSpy.apiQueryLogGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      clientIdentifier: 'aa:bb:cc:dd:ee:ff',
      clientProtocolVersion: 'IPv4',
    });
  });
});
