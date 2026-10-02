import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  DnsFilterListSourceEnum,
  FilterListEntriesService,
  FilterListsService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { FilterListEntryListComponent } from './filter-list-entry-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover a list's entries page: loading, read-only mode for a subscribed list,
 * deleting and the exception badge. TestBed with spy-backed entry and list APIs.
 */
describe('FilterListEntryListComponent', () => {
  let entriesApiSpy: {
    apiFilterListsListIdEntriesGet: jasmine.Spy;
    apiFilterListsListIdEntriesIdDelete: jasmine.Spy;
  };
  let filterListsApiSpy: { apiFilterListsIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(
    source: DnsFilterListSourceEnum,
    items: object[] = [{ id: 'entry-1', listId: 'list-1', value: 'bad.example', comment: null }],
  ) {
    entriesApiSpy = jasmine.createSpyObj('FilterListEntriesService', [
      'apiFilterListsListIdEntriesGet',
      'apiFilterListsListIdEntriesIdDelete',
    ]);
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', ['apiFilterListsIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    filterListsApiSpy.apiFilterListsIdGet.and.returnValue(
      of({ id: 'list-1', name: 'threat-intel', source }),
    );
    entriesApiSpy.apiFilterListsListIdEntriesGet.and.returnValue(
      of({
        items,
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [FilterListEntryListComponent],
      providers: [
        provideRouter([]),
        { provide: FilterListEntriesService, useValue: entriesApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ listId: 'list-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(FilterListEntryListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads entries and the owning list name', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Local);

    expect(entriesApiSpy.apiFilterListsListIdEntriesGet).toHaveBeenCalledWith({
      listId: 'list-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.entries().length).toBe(1);
    expect(fixture.componentInstance.listName()).toBe('threat-intel');
  });

  it('isReadOnly() is true for a Subscribed list', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Subscribed);

    expect(fixture.componentInstance.isReadOnly()).toBeTrue();
  });

  it('isReadOnly() is false for a Local list', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Local);

    expect(fixture.componentInstance.isReadOnly()).toBeFalse();
  });

  it('deleteEntry() does nothing at all for a Subscribed (read-only) list, without even opening the confirmation dialog', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Subscribed);

    await fixture.componentInstance.deleteEntry(fixture.componentInstance.entries()[0]);

    expect(fixture.componentInstance.confirmDialogVisible()).toBeFalse();
    expect(entriesApiSpy.apiFilterListsListIdEntriesIdDelete).not.toHaveBeenCalled();
  });

  it('deleteEntry() deletes and reloads once confirmed, for a Local list', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Local);
    entriesApiSpy.apiFilterListsListIdEntriesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteEntry(
      fixture.componentInstance.entries()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(entriesApiSpy.apiFilterListsListIdEntriesIdDelete).toHaveBeenCalledWith({
      listId: 'list-1',
      id: 'entry-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('marks an exception entry from a subscribed adblock list', async () => {
    const fixture = await createComponent(DnsFilterListSourceEnum.Subscribed, [
      { id: 'entry-1', listId: 'list-1', value: 'ads.example', isException: false },
      { id: 'entry-2', listId: 'list-1', value: 'cdn.ads.example', isException: true },
    ]);

    const rows: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('tr[cdk-row]'));

    expect(rows.length).toBe(2);
    expect(rows[0].textContent).not.toContain('Exception');
    expect(rows[1].textContent).toContain('Exception');
  });
});
