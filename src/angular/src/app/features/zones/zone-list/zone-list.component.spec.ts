import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ZonesService, ZoneTypeEnum } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneListComponent } from './zone-list.component';
import { AuthService } from '../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the zone list: loading a page, the enable/disable toggle, the confirmed delete
 * and which actions a user without `zones:write` sees. They use TestBed with spy-backed
 * `ZonesService` and `NotificationService` and a stubbed `AuthService.hasPermission`.
 */
describe('ZoneListComponent', () => {
  // The permissions the signed-in test user lacks; it holds every other one, so each test sees
  // the page as an administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  // A hand-shaped spy type rather than `jasmine.SpyObj<ZonesService>`: the generated methods have
  // one overload per `observe` value, and `SpyObj` takes the last one, which returns
  // `Observable<HttpEvent<T>>` instead of the plain body the component uses.
  let zonesApiSpy: {
    apiZonesGet: jasmine.Spy;
    apiZonesIdPatch: jasmine.Spy;
    apiZonesIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    zonesApiSpy = jasmine.createSpyObj('ZonesService', [
      'apiZonesGet',
      'apiZonesIdPatch',
      'apiZonesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    zonesApiSpy.apiZonesGet.and.returnValue(
      of({
        items: [
          {
            id: 'zone-1',
            name: 'example.com',
            type: ZoneTypeEnum.Primary,
            isDnssecSigned: false,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ZoneListComponent],
      providers: [
        // The template's RouterLink needs a Router and ActivatedRoute.
        provideRouter([]),
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ZoneListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('offers new, edit and delete, and an enabled status toggle, to a user with zones:write', async () => {
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('a[href="/zones/new"]')).not.toBeNull();
    expect(element.querySelector('a[href="/zones/zone-1/edit"]')).not.toBeNull();
    expect(element.querySelector('[aria-label="Delete zone"]')).not.toBeNull();
    expect(element.querySelector<HTMLButtonElement>('td button')?.disabled).toBeFalse();
  });

  it('hides new, edit and delete, and disables the status toggle, without zones:write', async () => {
    deniedPermissions = ['zones:write'];
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('a[href="/zones/new"]')).toBeNull();
    expect(element.querySelector('a[href="/zones/zone-1/edit"]')).toBeNull();
    expect(element.querySelector('[aria-label="Delete zone"]')).toBeNull();
    // The status stays visible: it is also the zone's only enabled/disabled indicator.
    expect(element.querySelector<HTMLButtonElement>('td button')?.textContent).toContain('Enabled');
    expect(element.querySelector<HTMLButtonElement>('td button')?.disabled).toBeTrue();
    // Read-only navigation to the zone's records is still offered.
    expect(element.querySelector('a[href="/zones/zone-1/records"]')).not.toBeNull();
  });

  it('loads the first page of zones on init', async () => {
    const fixture = await createComponent();

    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.zones().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 2, pageSize: 10 });

    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledWith({ skip: 20, take: 10 });
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    zonesApiSpy.apiZonesIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.zones()[0]);

    expect(zonesApiSpy.apiZonesIdPatch).toHaveBeenCalledWith({
      id: 'zone-1',
      body: { isDisabled: true },
    });
    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledTimes(2);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteZone() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteZone(
      fixture.componentInstance.zones()[0],
    );
    expect(fixture.componentInstance.confirmDialogVisible()).toBeTrue();
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(fixture.componentInstance.confirmDialogVisible()).toBeFalse();
    expect(zonesApiSpy.apiZonesIdDelete).not.toHaveBeenCalled();
  });

  it('deleteZone() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    zonesApiSpy.apiZonesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteZone(
      fixture.componentInstance.zones()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(zonesApiSpy.apiZonesIdDelete).toHaveBeenCalledWith({ id: 'zone-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification, without navigating away, when delete fails', async () => {
    const { HttpErrorResponse } = await import('@angular/common/http');
    const fixture = await createComponent();
    zonesApiSpy.apiZonesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteZone(
      fixture.componentInstance.zones()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
