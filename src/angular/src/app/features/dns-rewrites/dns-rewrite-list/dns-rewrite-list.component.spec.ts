import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ClientGroupsService,
  ClientsService,
  DnsRewritesService,
  DnsRewriteTargetKindEnum,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsRewriteListComponent } from './dns-rewrite-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the rewrite list: loading, the toggle, delete, errors, and the Safe Search
 * rewrites (labels by client or group name, no actions, the fallback label). TestBed with
 * spy-backed rewrite, client and group APIs and a fake `AuthService`.
 */
describe('DnsRewriteListComponent', () => {
  let rewritesApiSpy: {
    apiDnsRewritesGet: jasmine.Spy;
    apiDnsRewritesIdPatch: jasmine.Spy;
    apiDnsRewritesIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let clientsApiSpy: { apiClientsIdGet: jasmine.Spy };
  let clientGroupsApiSpy: { apiClientGroupsIdGet: jasmine.Spy };

  const GLOBAL_REWRITE = {
    id: 'rewrite-1',
    domain: 'host.example.com',
    target: '203.0.113.5',
    targetKind: DnsRewriteTargetKindEnum.A,
    isDisabled: false,
  };

  function scopeLookupProviders() {
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsIdGet']);
    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({ id: 'client-1', displayName: 'Kids tablet' }),
    );
    clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(of({ id: 'group-1', name: 'Kids' }));
    return [
      { provide: ClientsService, useValue: clientsApiSpy },
      { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
    ];
  }

  async function createComponent(items: unknown[] = [GLOBAL_REWRITE]) {
    rewritesApiSpy = jasmine.createSpyObj('DnsRewritesService', [
      'apiDnsRewritesGet',
      'apiDnsRewritesIdPatch',
      'apiDnsRewritesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    rewritesApiSpy.apiDnsRewritesGet.and.returnValue(
      of({ items, skip: 0, take: 25, totalCount: items.length }),
    );

    await TestBed.configureTestingModule({
      imports: [DnsRewriteListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsRewritesService, useValue: rewritesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        ...scopeLookupProviders(),
      ],
    }).compileComponents();

    // An administrator's view: every permission held.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);
    const fixture = TestBed.createComponent(DnsRewriteListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of rewrites on init', async () => {
    const fixture = await createComponent();

    expect(rewritesApiSpy.apiDnsRewritesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.rewrites().length).toBe(1);
    expect(fixture.componentInstance.rewrites()[0].targetKind).toBe(DnsRewriteTargetKindEnum.A);
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.rewrites()[0]);

    expect(rewritesApiSpy.apiDnsRewritesIdPatch).toHaveBeenCalledWith({
      id: 'rewrite-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRewrite() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(rewritesApiSpy.apiDnsRewritesIdDelete).toHaveBeenCalledWith({ id: 'rewrite-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRewrite() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(rewritesApiSpy.apiDnsRewritesIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it('shows an error notification when the initial load fails', async () => {
    rewritesApiSpy = jasmine.createSpyObj('DnsRewritesService', [
      'apiDnsRewritesGet',
      'apiDnsRewritesIdPatch',
      'apiDnsRewritesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    rewritesApiSpy.apiDnsRewritesGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    );

    await TestBed.configureTestingModule({
      imports: [DnsRewriteListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsRewritesService, useValue: rewritesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        ...scopeLookupProviders(),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsRewriteListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('boom');
  });

  it('labels a global rewrite as applying to all clients', async () => {
    const fixture = await createComponent();

    const cell: HTMLElement = fixture.nativeElement.querySelector('td.cdk-column-scope');
    expect(cell.textContent).toContain('All clients');
    expect(fixture.nativeElement.querySelector('[aria-label="Edit rewrite"]')).not.toBeNull();
  });

  it('labels Safe Search rewrites with the client or group they apply to, by name', async () => {
    const fixture = await createComponent([
      { ...GLOBAL_REWRITE, id: 'rewrite-2', clientId: 'client-1' },
      { ...GLOBAL_REWRITE, id: 'rewrite-3', clientGroupId: 'group-1' },
    ]);
    await flushMicrotasks();
    fixture.detectChanges();

    const cells: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('td.cdk-column-scope'),
    );
    expect(cells[0].textContent).toContain('Client');
    expect(cells[0].textContent).toContain('Kids tablet');
    expect(cells[1].textContent).toContain('Group');
    expect(cells[1].textContent).toContain('Kids');
    expect(clientsApiSpy.apiClientsIdGet).toHaveBeenCalledWith({ id: 'client-1' });
  });

  it('offers no edit, delete or toggle on a Safe Search rewrite', async () => {
    const fixture = await createComponent([
      { ...GLOBAL_REWRITE, id: 'rewrite-2', clientId: 'client-1' },
    ]);

    expect(fixture.nativeElement.querySelector('[aria-label="Edit rewrite"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Delete rewrite"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Safe Search');
    const toggle: HTMLButtonElement = fixture.nativeElement.querySelector(
      'td.cdk-column-disabled button',
    );
    expect(toggle.disabled).toBeTrue();
  });

  it('falls back to a generic label when the client cannot be looked up', async () => {
    const fixture = await createComponent([
      { ...GLOBAL_REWRITE, id: 'rewrite-2', clientId: 'client-1' },
    ]);
    // Swap in a failing lookup and reload, so the second lookup is the one refused.
    clientsApiSpy.apiClientsIdGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );
    await fixture.componentInstance.onPageChange({ pageIndex: 0, pageSize: 25 });
    await flushMicrotasks();
    fixture.detectChanges();

    const cell: HTMLElement = fixture.nativeElement.querySelector('td.cdk-column-scope');
    expect(cell.textContent).toContain('a client');
  });
});
