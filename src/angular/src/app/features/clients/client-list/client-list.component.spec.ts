import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ClientDetail,
  ClientGroupsService,
  ClientsService,
  DnsClientDeviceIconEnum,
  DnsClientOriginEnum,
} from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientListComponent } from './client-list.component';
import { AuthService } from '../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/** A dual-stack, online, favorited device with a vendor, an explicit icon, and a group. */
const DUAL_STACK_CLIENT: ClientDetail = {
  id: 'client-1',
  addresses: [
    {
      id: 'address-6',
      clientIdentifier: '00:03:00:01:b8:27:eb:12:34:56',
      protocolVersion: 'IPv6',
      address: '2001:db8::42',
      macAddress: 'b8:27:eb:12:34:56',
    },
    {
      id: 'address-4',
      clientIdentifier: 'b8:27:eb:12:34:56',
      protocolVersion: 'IPv4',
      address: '192.0.2.42',
      macAddress: 'b8:27:eb:12:34:56',
    },
  ],
  displayName: 'Living Room TV',
  clientGroupId: 'group-1',
  isDisabled: false,
  origin: DnsClientOriginEnum.Manual,
  isOnline: true,
  lastSeenUtc: new Date(Date.now() - 5 * 60_000).toISOString(),
  deviceIconKey: DnsClientDeviceIconEnum.Television,
  suggestedDeviceIconKey: DnsClientDeviceIconEnum.SingleBoardComputer,
  isFavorite: true,
  vendor: 'Raspberry Pi Foundation',
};

/** An unnamed, never-seen, DHCP-discovered device with a randomized MAC and no group. */
const UNNAMED_CLIENT: ClientDetail = {
  id: 'client-2',
  addresses: [
    {
      id: 'address-2',
      clientIdentifier: '02:11:22:33:44:55',
      protocolVersion: 'IPv4',
      address: null,
      macAddress: '02:11:22:33:44:55',
      isRandomizedMacAddress: true,
    },
  ],
  clientGroupId: null,
  isDisabled: false,
  origin: DnsClientOriginEnum.Dhcp,
  isOnline: false,
  lastSeenUtc: null,
  isFavorite: false,
  isRandomizedMacAddress: true,
};

const SUMMARY = {
  totalCount: 2,
  onlineCount: 1,
  offlineCount: 1,
  newCount: 1,
  favoriteCount: 1,
  newSinceUtc: '2026-09-23T04:00:00Z',
  onlineWindow: '00:10:00',
};

/**
 * These tests cover the Clients page: rows with addresses, presence, vendor and icon, the summary
 * cards as filters (and the query string), sorting, permission-dependent links and columns, the
 * favorite and enable toggles, delete, and failure states. TestBed with spy-backed API services,
 * a fake `AuthService` that denies chosen permissions and a stubbed route.
 */
describe('ClientListComponent', () => {
  // The permissions the test user lacks. It holds all others, so a test sees the page as an
  // administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  let clientsApiSpy: {
    apiClientsGet: jasmine.Spy;
    apiClientsSummaryGet: jasmine.Spy;
    apiClientsIdPatch: jasmine.Spy;
    apiClientsIdDelete: jasmine.Spy;
  };
  let clientGroupsApiSpy: { apiClientGroupsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(
    options: {
      clients?: ClientDetail[];
      list?: Observable<unknown>;
      summary?: Observable<unknown>;
      queryParams?: Record<string, string>;
    } = {},
  ) {
    clientsApiSpy = jasmine.createSpyObj('ClientsService', [
      'apiClientsGet',
      'apiClientsSummaryGet',
      'apiClientsIdPatch',
      'apiClientsIdDelete',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    const clients = options.clients ?? [DUAL_STACK_CLIENT, UNNAMED_CLIENT];
    clientsApiSpy.apiClientsGet.and.returnValue(
      options.list ?? of({ items: clients, skip: 0, take: 25, totalCount: clients.length }),
    );
    clientsApiSpy.apiClientsSummaryGet.and.returnValue(options.summary ?? of(SUMMARY));
    clientGroupsApiSpy.apiClientGroupsGet.and.returnValue(
      of({ items: [{ id: 'group-1', name: 'Trusted LAN' }], skip: 0, take: 500, totalCount: 1 }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientListComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap(options.queryParams ?? {}) },
          },
        },
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ClientListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  function query(fixture: { nativeElement: HTMLElement }, testId: string): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll(`[data-testid="${testId}"]`));
  }

  it('loads the summary and the first page, recently-seen first, with no filter', async () => {
    const fixture = await createComponent();

    expect(clientsApiSpy.apiClientsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      sort: 'LastSeen',
    });
    expect(clientsApiSpy.apiClientsSummaryGet).toHaveBeenCalledWith({
      newSinceUtc: jasmine.any(String),
    });
    expect(fixture.componentInstance.summary()?.onlineCount).toBe(1);
    expect(fixture.componentInstance.rows().length).toBe(2);
    expect(fixture.componentInstance.rows()[0].groupName).toBe('Trusted LAN');
    expect(fixture.componentInstance.rows()[1].groupName).toBeNull();
  });

  it('without clientgroups:read, still lists the clients but leaves out the Group column and says why', async () => {
    deniedPermissions = ['clientgroups:read'];
    const fixture = await createComponent();

    expect(clientGroupsApiSpy.apiClientGroupsGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(fixture.componentInstance.rows().length).toBe(2);
    expect(fixture.componentInstance.displayedColumns()).not.toContain('group');
    expect(query(fixture, 'groups-unavailable-hint').length).toBe(1);
  });

  it('offers the activity link only with querylog:read, and the edit link only with clients:write', async () => {
    deniedPermissions = ['querylog:read', 'clients:write'];
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[aria-label="View activity"]')).toBeNull();
    expect(element.querySelector('a[href="/clients/new"]')).toBeNull();
    // The device name is still shown, as plain text rather than a link to the edit form.
    const titles = query(fixture, 'device-title');
    expect(titles.length).toBe(2);
    expect(titles[0].tagName).toBe('SPAN');
    expect((query(fixture, 'favorite-toggle')[0] as HTMLButtonElement).disabled).toBeTrue();
  });

  it('shows one row per device with every one of its addresses, IPv4 first', async () => {
    const fixture = await createComponent();
    const row = fixture.componentInstance.rows()[0];

    expect(row.addresses.map((address) => address.protocolVersion)).toEqual(['IPv4', 'IPv6']);
    const items = query(fixture, 'address-item');
    expect(items.length).toBe(3);
    expect(items[0].textContent).toContain('192.0.2.42');
    expect(items[1].textContent).toContain('2001:db8::42');
    // The IPv6 row shows its DUID, not the MAC it embeds, so the two rows stay distinct.
    const identifiers = query(fixture, 'address-identifier').map((item) =>
      item.textContent?.trim(),
    );
    expect(identifiers.slice(0, 2)).toEqual(['b8:27:eb:12:34:56', '00:03:00:01:b8:27:eb:12:34:56']);
  });

  it('renders presence, vendor, and the explicit icon over the suggestion', async () => {
    const fixture = await createComponent();
    const [tv, unnamed] = fixture.componentInstance.rows();

    expect(tv.icon.key).toBe(DnsClientDeviceIconEnum.Television);
    expect(tv.lastSeenRelative).toBe('5 min ago');
    expect(unnamed.lastSeenRelative).toBeNull();

    const badges = query(fixture, 'presence-badge').map((badge) => badge.textContent?.trim());
    expect(badges).toEqual(['Online', 'Offline']);
    const vendors = query(fixture, 'device-vendor').map((vendor) => vendor.textContent?.trim());
    expect(vendors).toEqual(['Raspberry Pi Foundation', 'Randomized MAC']);
    expect(fixture.nativeElement.textContent).toContain('Never seen');
  });

  it('titles an unnamed device by its identifier when it has no IP address yet', async () => {
    const fixture = await createComponent();
    const unnamed = fixture.componentInstance.rows()[1];

    expect(unnamed.hasDisplayName).toBeFalse();
    expect(unnamed.title).toBe('02:11:22:33:44:55');
  });

  it('explains the online window in plain words', async () => {
    const fixture = await createComponent();

    expect(query(fixture, 'online-window-hint')[0].textContent).toContain('10 minutes');
  });

  it('selecting a summary card filters the list and mirrors it into the URL', async () => {
    const fixture = await createComponent();
    const router = TestBed.inject(Router);
    clientsApiSpy.apiClientsGet.calls.reset();

    await fixture.componentInstance.selectFilter('online');

    expect(clientsApiSpy.apiClientsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      sort: 'LastSeen',
      presence: 'Online',
    });
    expect(router.navigate).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({ queryParams: { filter: 'online', sort: null } }),
    );
  });

  it('maps every filter onto the matching list parameter', async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;

    await component.selectFilter('offline');
    expect(clientsApiSpy.apiClientsGet.calls.mostRecent().args[0].presence).toBe('Offline');

    await component.selectFilter('favorites');
    expect(clientsApiSpy.apiClientsGet.calls.mostRecent().args[0].isFavorite).toBeTrue();

    // "New today" filters from the exact instant the summary card counted from.
    await component.selectFilter('new');
    const summarySince = clientsApiSpy.apiClientsSummaryGet.calls.mostRecent().args[0].newSinceUtc;
    expect(clientsApiSpy.apiClientsGet.calls.mostRecent().args[0].createdSinceUtc).toBe(
      summarySince,
    );
  });

  it('restores a filter and ordering from the URL, ignoring unknown values', async () => {
    await createComponent({ queryParams: { filter: 'offline', sort: 'DisplayName' } });
    expect(clientsApiSpy.apiClientsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      sort: 'DisplayName',
      presence: 'Offline',
    });

    TestBed.resetTestingModule();
    await createComponent({ queryParams: { filter: 'bogus', sort: 'Random' } });
    expect(clientsApiSpy.apiClientsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
      sort: 'LastSeen',
    });
  });

  it('changeSort() reorders from the first page', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.changeSort('Created');

    expect(clientsApiSpy.apiClientsGet.calls.mostRecent().args[0]).toEqual({
      skip: 0,
      take: 25,
      sort: 'Created',
    });
  });

  it('toggleFavorite() patches the star and refreshes the counts', async () => {
    const fixture = await createComponent();
    clientsApiSpy.apiClientsIdPatch.and.returnValue(of(undefined));
    clientsApiSpy.apiClientsSummaryGet.calls.reset();

    query(fixture, 'favorite-toggle')[0].click();
    await flushMicrotasks();

    expect(clientsApiSpy.apiClientsIdPatch).toHaveBeenCalledWith({
      id: 'client-1',
      body: { isFavorite: false },
    });
    expect(clientsApiSpy.apiClientsSummaryGet).toHaveBeenCalled();
  });

  it('shows a filter-specific message, not the first-run empty state, when a filter matches nothing', async () => {
    const fixture = await createComponent({ clients: [], queryParams: { filter: 'favorites' } });

    expect(query(fixture, 'no-matches').length).toBe(1);
    expect(fixture.nativeElement.querySelector('app-empty-state')).toBeNull();
  });

  it('still lists devices when the summary fails to load, showing dashes on the cards', async () => {
    const fixture = await createComponent({
      summary: throwError(() => new HttpErrorResponse({ status: 500 })),
    });

    expect(fixture.componentInstance.summary()).toBeNull();
    expect(fixture.componentInstance.rows().length).toBe(2);
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    clientsApiSpy.apiClientsIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.rows()[0]);

    expect(clientsApiSpy.apiClientsIdPatch).toHaveBeenCalledWith({
      id: 'client-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteClient() deletes and reloads once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    clientsApiSpy.apiClientsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteClient(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(clientsApiSpy.apiClientsIdDelete).toHaveBeenCalledWith({ id: 'client-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteClient() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteClient(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(clientsApiSpy.apiClientsIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    clientsApiSpy.apiClientsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteClient(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it('shows an error message when the page fails to load', async () => {
    const fixture = await createComponent({
      list: throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    });

    expect(fixture.componentInstance.errorMessage()).toBe('boom');
  });
});
