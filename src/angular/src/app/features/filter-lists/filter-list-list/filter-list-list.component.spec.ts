import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  DnsFilterListActionEnum,
  DnsFilterListKindEnum,
  DnsFilterListSourceEnum,
  FilterListsService,
} from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { FilterListListComponent } from './filter-list-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the filter list page: loading and paging, the toggle, delete with its
 * confirmation and failure, and the last-fetched column. TestBed with spy-backed
 * `FilterListsService` and `NotificationService`.
 */
describe('FilterListListComponent', () => {
  // A hand-written spy type rather than `jasmine.SpyObj<FilterListsService>`; the AuthService
  // spec explains why (overloaded generated methods).
  let filterListsApiSpy: {
    apiFilterListsGet: jasmine.Spy;
    apiFilterListsIdPatch: jasmine.Spy;
    apiFilterListsIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', [
      'apiFilterListsGet',
      'apiFilterListsIdPatch',
      'apiFilterListsIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({
        items: [
          {
            id: 'list-1',
            name: 'threat-intel',
            kind: DnsFilterListKindEnum.Domain,
            action: DnsFilterListActionEnum.Block,
            source: DnsFilterListSourceEnum.Local,
            isDisabled: false,
          },
          {
            id: 'list-2',
            name: 'community-ads',
            kind: DnsFilterListKindEnum.Domain,
            action: DnsFilterListActionEnum.Block,
            source: DnsFilterListSourceEnum.Subscribed,
            subscriptionUrl: 'https://lists.example/ads.txt',
            lastFetchedAtUtc: '2026-09-20T03:00:00Z',
            isDisabled: false,
          },
          {
            id: 'list-3',
            name: 'new-subscription',
            kind: DnsFilterListKindEnum.Domain,
            action: DnsFilterListActionEnum.Block,
            source: DnsFilterListSourceEnum.Subscribed,
            subscriptionUrl: 'https://lists.example/new.txt',
            lastFetchedAtUtc: null,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 3,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [FilterListListComponent],
      providers: [
        provideRouter([]),
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(FilterListListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of filter lists on init', async () => {
    const fixture = await createComponent();

    expect(filterListsApiSpy.apiFilterListsGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.lists().length).toBe(3);
    expect(fixture.componentInstance.totalCount()).toBe(3);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(filterListsApiSpy.apiFilterListsGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    filterListsApiSpy.apiFilterListsIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.lists()[0]);

    expect(filterListsApiSpy.apiFilterListsIdPatch).toHaveBeenCalledWith({
      id: 'list-1',
      body: { isDisabled: true },
    });
    expect(filterListsApiSpy.apiFilterListsGet).toHaveBeenCalledTimes(2);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteList() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteList(
      fixture.componentInstance.lists()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(filterListsApiSpy.apiFilterListsIdDelete).not.toHaveBeenCalled();
  });

  it('deleteList() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    filterListsApiSpy.apiFilterListsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteList(
      fixture.componentInstance.lists()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(filterListsApiSpy.apiFilterListsIdDelete).toHaveBeenCalledWith({ id: 'list-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(filterListsApiSpy.apiFilterListsGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    filterListsApiSpy.apiFilterListsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteList(
      fixture.componentInstance.lists()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
  it('shows when each subscribed list was last fetched, and flags one never fetched', async () => {
    const fixture = await createComponent();
    const fetched = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.fetched'),
      (element) => element.textContent?.trim(),
    );

    expect(fetched.length).toBe(2);
    expect(fetched[0]).toMatch(/^Fetched /);
    expect(fetched[0]).not.toContain('2026-09-20T03:00:00Z');
    expect(fetched[1]).toBe('Not fetched yet');
  });
});
