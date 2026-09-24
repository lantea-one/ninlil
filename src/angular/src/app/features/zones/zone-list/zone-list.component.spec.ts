import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ZonesService, ZoneTypeEnum } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneListComponent } from './zone-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ZoneListComponent', () => {
  // A manually-shaped spy interface, not `jasmine.SpyObj<ZonesService>` -- the generated
  // `ZonesService`'s own methods are each declared with 4 overloads (one per `observe` value plus
  // the implementation signature), and `jasmine.SpyObj<T>` picks the *last* overload it sees when
  // deriving each spy's own call signature, which is the `observe: 'events'` one returning
  // `Observable<HttpEvent<T>>` rather than the plain-body `Observable<T>` this component and every
  // test double below actually deal in. `dashboard-stats.service.spec.ts` already established
  // this same loosely-typed-spy convention for exactly this reason.
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
        // ZoneListComponent's own template uses RouterLink, which injects ActivatedRoute/Router
        // internally regardless of whether this component reads either itself -- provideRouter([])
        // registers both, matching app.spec.ts's/forbidden.component.spec.ts's own precedent.
        provideRouter([]),
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ZoneListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

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
