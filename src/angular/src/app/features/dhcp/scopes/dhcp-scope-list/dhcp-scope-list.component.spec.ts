import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DhcpScopesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpScopeListComponent } from './dhcp-scope-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DhcpScopeListComponent', () => {
  let scopesApiSpy: {
    apiDhcpScopesGet: jasmine.Spy;
    apiDhcpScopesIdPatch: jasmine.Spy;
    apiDhcpScopesIdDelete: jasmine.Spy;
    apiDhcpScopesIdStatisticsGet: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
      'apiDhcpScopesGet',
      'apiDhcpScopesIdPatch',
      'apiDhcpScopesIdDelete',
      'apiDhcpScopesIdStatisticsGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({
        items: [
          {
            id: 'scope-1',
            name: 'lan',
            protocolVersion: 'IPv4',
            kind: 'AddressAssignment',
            rangeStartAddress: '192.168.1.100',
            rangeEndAddress: '192.168.1.200',
            subnetPrefixLength: 24,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );
    scopesApiSpy.apiDhcpScopesIdStatisticsGet.and.returnValue(
      of({
        scopeId: 'scope-1',
        scopeName: 'lan',
        protocolVersion: 'IPv4',
        kind: 'AddressAssignment',
        totalSlots: '101',
        leasedCount: 12,
        offeredCount: 1,
        reservedCount: 3,
        availableSlots: '85',
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpScopeListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpScopeListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of scopes on init', async () => {
    const fixture = await createComponent();

    expect(scopesApiSpy.apiDhcpScopesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.scopes().length).toBe(1);
  });

  it("carries each scope's own kind through to the list, for the Kind column", async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.scopes()[0].kind).toBe('AddressAssignment');
    expect(fixture.componentInstance.displayedColumns).toContain('kind');
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    scopesApiSpy.apiDhcpScopesIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.scopes()[0]);

    expect(scopesApiSpy.apiDhcpScopesIdPatch).toHaveBeenCalledWith({
      id: 'scope-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteScope() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    scopesApiSpy.apiDhcpScopesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteScope(
      fixture.componentInstance.scopes()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(scopesApiSpy.apiDhcpScopesIdDelete).toHaveBeenCalledWith({ id: 'scope-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteScope() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteScope(
      fixture.componentInstance.scopes()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(scopesApiSpy.apiDhcpScopesIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    scopesApiSpy.apiDhcpScopesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteScope(
      fixture.componentInstance.scopes()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it("loads each scope's own real utilization after the page itself loads", async () => {
    const fixture = await createComponent();

    expect(scopesApiSpy.apiDhcpScopesIdStatisticsGet).toHaveBeenCalledWith({ id: 'scope-1' });
    const statistics = fixture.componentInstance.statisticsByScopeId().get('scope-1');
    expect(statistics).toBeDefined();
    expect(statistics?.leasedCount).toBe(12);
    expect(statistics?.totalSlots).toBe('101');
  });

  it('utilizationText() formats a compact, exact summary using bigint arithmetic', async () => {
    const fixture = await createComponent();
    const statistics = fixture.componentInstance.statisticsByScopeId().get('scope-1')!;

    expect(fixture.componentInstance.utilizationText(statistics)).toBe(
      '12 leased, 1 offered, 3 reserved, 85 available of 101',
    );
  });

  it('utilizationText() leaves out offered and reserved counts of zero', async () => {
    const fixture = await createComponent();
    const statistics = fixture.componentInstance.statisticsByScopeId().get('scope-1')!;

    expect(
      fixture.componentInstance.utilizationText({
        ...statistics,
        offeredCount: 0,
        reservedCount: 0,
      }),
    ).toBe('12 leased, 85 available of 101');
  });

  it('utilizationColor() reports success/warning/danger/secondary correctly, including at astronomical IPv6 scale', async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;

    const totalV6 = (2n ** 64n).toString(); // an ordinary /64's own real total
    expect(
      component.utilizationColor({
        totalSlots: totalV6,
        availableSlots: totalV6,
        leasedCount: 0,
        offeredCount: 0,
        reservedCount: 0,
      } as never),
    ).toBe('success');
    expect(
      component.utilizationColor({
        totalSlots: '100',
        availableSlots: '3',
        leasedCount: 97,
        offeredCount: 0,
        reservedCount: 0,
      } as never),
    ).toBe('danger');
    expect(
      component.utilizationColor({
        totalSlots: '100',
        availableSlots: '15',
        leasedCount: 85,
        offeredCount: 0,
        reservedCount: 0,
      } as never),
    ).toBe('warning');
    expect(
      component.utilizationColor({
        totalSlots: '0',
        availableSlots: '0',
        leasedCount: 0,
        offeredCount: 0,
        reservedCount: 0,
      } as never),
    ).toBe('secondary');
  });

  it('a failed per-scope statistics fetch leaves that row blank rather than failing the whole list', async () => {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
      'apiDhcpScopesGet',
      'apiDhcpScopesIdPatch',
      'apiDhcpScopesIdDelete',
      'apiDhcpScopesIdStatisticsGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({
        items: [{ id: 'scope-1', name: 'lan', protocolVersion: 'IPv4', isDisabled: false }],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );
    scopesApiSpy.apiDhcpScopesIdStatisticsGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpScopeListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpScopeListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.scopes().length).toBe(1);
    expect(fixture.componentInstance.statisticsByScopeId().has('scope-1')).toBe(false);
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });
});
