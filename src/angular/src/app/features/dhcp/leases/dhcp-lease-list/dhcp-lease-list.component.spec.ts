import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DhcpLeasesService, DhcpScopesService } from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpLeaseListComponent } from './dhcp-lease-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DHCP Leases page: loading, the scope filter and paging, refresh, and
 * force-release with its permission check and confirmation. TestBed with spy-backed lease and
 * scope APIs, `AuthService` and `NotificationService`.
 */
describe('DhcpLeaseListComponent', () => {
  let leasesApiSpy: { apiDhcpLeasesGet: jasmine.Spy; apiDhcpLeasesIdReleasePost: jasmine.Spy };
  let scopesApiSpy: { apiDhcpScopesGet: jasmine.Spy };
  let authServiceSpy: { hasPermission: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(canManage = true) {
    leasesApiSpy = jasmine.createSpyObj('DhcpLeasesService', [
      'apiDhcpLeasesGet',
      'apiDhcpLeasesIdReleasePost',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesGet']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.returnValue(canManage);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({ items: [{ id: 'scope-1', name: 'lan' }], skip: 0, take: 200, totalCount: 1 }),
    );
    leasesApiSpy.apiDhcpLeasesGet.and.returnValue(
      of({
        items: [
          {
            id: 'lease-1',
            protocolVersion: 'IPv4',
            scopeName: 'lan',
            clientIdentifier: 'aa:bb:cc:dd:ee:ff',
            ipAddress: '192.168.1.50',
            state: 'Active',
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpLeaseListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpLeasesService, useValue: leasesApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpLeaseListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads all-scopes leases on init with no scope filter', async () => {
    const fixture = await createComponent();

    expect(leasesApiSpy.apiDhcpLeasesGet).toHaveBeenCalledWith({
      scopeId: undefined,
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.leases().length).toBe(1);
  });

  it('search() re-issues the query with the current scope filter and resets to the first page', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.pageIndex.set(2);
    fixture.componentInstance.filterForm.controls.scopeId.setValue('scope-1');

    await fixture.componentInstance.search();

    expect(leasesApiSpy.apiDhcpLeasesGet).toHaveBeenCalledWith({
      scopeId: 'scope-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.pageIndex()).toBe(0);
  });

  it('refresh() reloads the current page without changing filters or position', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.pageIndex.set(1);

    await fixture.componentInstance.refresh();

    expect(leasesApiSpy.apiDhcpLeasesGet).toHaveBeenCalledWith({
      scopeId: undefined,
      skip: 25,
      take: 25,
    });
  });

  it("shows the prefixLength column and carries a Prefix Delegation lease's own value through", async () => {
    leasesApiSpy = jasmine.createSpyObj('DhcpLeasesService', [
      'apiDhcpLeasesGet',
      'apiDhcpLeasesIdReleasePost',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesGet']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.returnValue(true);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({ items: [{ id: 'scope-1', name: 'prefixes' }], skip: 0, take: 200, totalCount: 1 }),
    );
    leasesApiSpy.apiDhcpLeasesGet.and.returnValue(
      of({
        items: [
          {
            id: 'lease-1',
            protocolVersion: 'IPv6',
            scopeName: 'prefixes',
            clientIdentifier: '00:01:00:01:aa:bb:cc:dd:ee:ff',
            ipAddress: '2001:db8:1234::',
            prefixLength: 60,
            state: 'Active',
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpLeaseListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpLeasesService, useValue: leasesApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpLeaseListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.displayedColumns).toContain('prefixLength');
    expect(fixture.componentInstance.leases()[0].prefixLength).toBe(60);
  });

  it('hides the release button entirely for a session lacking dhcpleases:manage', async () => {
    const fixture = await createComponent(false);

    expect(fixture.componentInstance.canManage).toBe(false);
    const releaseButtons = fixture.nativeElement.querySelectorAll(
      'button[aria-label^="Release lease"]',
    );
    expect(releaseButtons.length).toBe(0);
  });

  it('release() confirms first, then calls the release endpoint and replaces the lease row', async () => {
    const fixture = await createComponent();
    leasesApiSpy.apiDhcpLeasesIdReleasePost.and.returnValue(
      of({
        id: 'lease-1',
        protocolVersion: 'IPv4',
        scopeName: 'lan',
        clientIdentifier: 'aa:bb:cc:dd:ee:ff',
        ipAddress: '192.168.1.50',
        state: 'Released',
      }),
    );

    const releasePromise = fixture.componentInstance.release(fixture.componentInstance.leases()[0]);
    await flushMicrotasks();
    expect(leasesApiSpy.apiDhcpLeasesIdReleasePost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.confirmDialogVisible()).toBe(true);

    fixture.componentInstance.onConfirmDialogResolved(true);
    await releasePromise;

    expect(leasesApiSpy.apiDhcpLeasesIdReleasePost).toHaveBeenCalledWith({ id: 'lease-1' });
    expect(fixture.componentInstance.leases()[0].state).toBe('Released');
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('release() does nothing further when the operator dismisses the confirmation dialog', async () => {
    const fixture = await createComponent();

    const releasePromise = fixture.componentInstance.release(fixture.componentInstance.leases()[0]);
    await flushMicrotasks();
    fixture.componentInstance.onConfirmDialogResolved(false);
    await releasePromise;

    expect(leasesApiSpy.apiDhcpLeasesIdReleasePost).not.toHaveBeenCalled();
  });

  it('release() reports an error via the notification service when the endpoint fails', async () => {
    const fixture = await createComponent();
    leasesApiSpy.apiDhcpLeasesIdReleasePost.and.returnValue(throwError(() => new Error('boom')));

    const releasePromise = fixture.componentInstance.release(fixture.componentInstance.leases()[0]);
    await flushMicrotasks();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await releasePromise;

    expect(notificationSpy.error).toHaveBeenCalled();
  });
});
