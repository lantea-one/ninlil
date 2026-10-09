import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnsCacheService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsCacheCardComponent } from './dns-cache-card.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNS cache card: statistics, which actions a read-only user sees, flushing
 * one or every cache after confirmation, removing a name and errors. TestBed with a spy-backed
 * `DnsCacheService`, `NotificationService` and a stubbed `AuthService.hasPermission`.
 */
describe('DnsCacheCardComponent', () => {
  let apiSpy: {
    apiDnsCacheGet: jasmine.Spy;
    apiDnsCacheDelete: jasmine.Spy;
    apiDnsCacheEntriesNameDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  const caches = [
    { name: 'dnssec', entryCount: 4, maxEntries: 10000, hits: 36, misses: 4 },
    { name: 'forwarded', entryCount: 1842, maxEntries: 10000, hits: 900, misses: 100 },
    { name: 'recursive', entryCount: 0, maxEntries: 100000, hits: 0, misses: 0 },
  ];

  async function createComponent(permissions: string[], configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('DnsCacheService', [
      'apiDnsCacheGet',
      'apiDnsCacheDelete',
      'apiDnsCacheEntriesNameDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    apiSpy.apiDnsCacheGet.and.returnValue(of(caches));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [DnsCacheCardComponent],
      providers: [
        { provide: DnsCacheService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: AuthService,
          useValue: { hasPermission: (permission: string) => permissions.includes(permission) },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsCacheCardComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  /** This function answers the confirm dialog the next flush opens. */
  async function confirmNextDialog(
    fixture: Awaited<ReturnType<typeof createComponent>>,
    confirmed: boolean,
  ): Promise<void> {
    await flushMicrotasks();
    fixture.componentInstance.onConfirmDialogResolved(confirmed);
  }

  it('shows every cache with its size and hit rate', async () => {
    const fixture = await createComponent(['recursivesettings:read']);
    const element: HTMLElement = fixture.nativeElement;

    const forwarded = element.querySelector('tr[data-cache="forwarded"]')?.textContent ?? '';
    expect(forwarded).toContain('Forwarded answers');
    expect(forwarded).toContain('1842');
    expect(forwarded).toContain('90%');

    const recursive = element.querySelector('tr[data-cache="recursive"]')?.textContent ?? '';
    expect(recursive).toContain('Recursive resolver');
    expect(recursive).toContain('—');

    const dnssec = element.querySelector('tr[data-cache="dnssec"]')?.textContent ?? '';
    expect(dnssec).toContain('DNSSEC trust');
    expect(dnssec).toContain('90%');
  });

  it('hides every write action from a read-only user', async () => {
    const fixture = await createComponent(['recursivesettings:read']);
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('tr[data-cache] button')).toBeNull();
    expect(element.querySelector('#dns-cache-remove-name')).toBeNull();
    expect(element.textContent).not.toContain('Flush all');
  });

  it('shows the write actions to a user who may change the cache', async () => {
    const fixture = await createComponent(['recursivesettings:read', 'recursivesettings:write']);
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelectorAll('tr[data-cache] button').length).toBe(3);
    expect(element.querySelector('#dns-cache-remove-name')).not.toBeNull();
    expect(element.textContent).toContain('Flush all');
  });

  it('flushes one cache once confirmed, then reloads the statistics', async () => {
    const fixture = await createComponent(['recursivesettings:write'], () => {
      apiSpy.apiDnsCacheDelete.and.returnValue(of({ removedEntryCount: 1842 }));
    });

    const flushing = fixture.componentInstance.flush(caches[1]);
    await confirmNextDialog(fixture, true);
    await flushing;

    expect(apiSpy.apiDnsCacheDelete).toHaveBeenCalledWith({ cache: 'forwarded' });
    expect(notificationSpy.success).toHaveBeenCalledWith('Flushed 1842 entries.');
    expect(apiSpy.apiDnsCacheGet).toHaveBeenCalledTimes(2);
  });

  it('flushes every cache when no cache is named', async () => {
    const fixture = await createComponent(['recursivesettings:write'], () => {
      apiSpy.apiDnsCacheDelete.and.returnValue(of({ removedEntryCount: 1 }));
    });

    const flushing = fixture.componentInstance.flush();
    await confirmNextDialog(fixture, true);
    await flushing;

    expect(apiSpy.apiDnsCacheDelete).toHaveBeenCalledWith({});
    expect(notificationSpy.success).toHaveBeenCalledWith('Flushed 1 entry.');
  });

  it('does nothing when the flush is not confirmed', async () => {
    const fixture = await createComponent(['recursivesettings:write']);

    const flushing = fixture.componentInstance.flush(caches[0]);
    await confirmNextDialog(fixture, false);
    await flushing;

    expect(apiSpy.apiDnsCacheDelete).not.toHaveBeenCalled();
  });

  it('removes one name and clears the field', async () => {
    const fixture = await createComponent(['recursivesettings:write'], () => {
      apiSpy.apiDnsCacheEntriesNameDelete.and.returnValue(of({ removedEntryCount: 2 }));
    });
    const component = fixture.componentInstance;

    component.removeForm.setValue({ name: '  www.example.com ' });
    await component.removeName();

    expect(apiSpy.apiDnsCacheEntriesNameDelete).toHaveBeenCalledWith({ name: 'www.example.com' });
    expect(notificationSpy.success).toHaveBeenCalledWith('Removed 2 entries for www.example.com.');
    expect(component.removeForm.controls.name.value).toBe('');
  });

  it('says so when a removed name was not cached', async () => {
    const fixture = await createComponent(['recursivesettings:write'], () => {
      apiSpy.apiDnsCacheEntriesNameDelete.and.returnValue(of({ removedEntryCount: 0 }));
    });

    fixture.componentInstance.removeForm.setValue({ name: 'nothing.example' });
    await fixture.componentInstance.removeName();

    expect(notificationSpy.success).toHaveBeenCalledWith('Nothing was cached for nothing.example.');
  });

  it('refuses to submit a blank name', async () => {
    const fixture = await createComponent(['recursivesettings:write']);

    fixture.componentInstance.removeForm.setValue({ name: '   ' });
    await fixture.componentInstance.removeName();

    expect(apiSpy.apiDnsCacheEntriesNameDelete).not.toHaveBeenCalled();
  });

  it('reports a failed removal', async () => {
    const fixture = await createComponent(['recursivesettings:write'], () => {
      apiSpy.apiDnsCacheEntriesNameDelete.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'not a name' })),
      );
    });

    fixture.componentInstance.removeForm.setValue({ name: 'bad..name' });
    await fixture.componentInstance.removeName();

    expect(notificationSpy.error).toHaveBeenCalledWith('not a name');
  });

  it('shows an error when the statistics cannot be loaded', async () => {
    const fixture = await createComponent(['recursivesettings:read'], () => {
      apiSpy.apiDnsCacheGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
      );
    });

    expect(fixture.componentInstance.errorMessage()).toBe('boom');
  });
});
