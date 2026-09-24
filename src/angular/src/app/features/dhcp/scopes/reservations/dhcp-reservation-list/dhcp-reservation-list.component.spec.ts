import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DhcpReservationsService, DhcpScopesService } from '../../../../../api-client';
import { NotificationService } from '../../../../../shared/notification/notification.service';
import { DhcpReservationListComponent } from './dhcp-reservation-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DhcpReservationListComponent', () => {
  let reservationsApiSpy: {
    apiDhcpScopesScopeIdReservationsGet: jasmine.Spy;
    apiDhcpScopesScopeIdReservationsIdDelete: jasmine.Spy;
  };
  let scopesApiSpy: { apiDhcpScopesIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    reservationsApiSpy = jasmine.createSpyObj('DhcpReservationsService', [
      'apiDhcpScopesScopeIdReservationsGet',
      'apiDhcpScopesScopeIdReservationsIdDelete',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(of({ id: 'scope-1', name: 'lan' }));
    reservationsApiSpy.apiDhcpScopesScopeIdReservationsGet.and.returnValue(
      of({
        items: [
          {
            id: 'res-1',
            scopeId: 'scope-1',
            clientIdentifier: 'aa:bb:cc:dd:ee:ff',
            ipAddress: '192.168.1.50',
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpReservationListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpReservationsService, useValue: reservationsApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ scopeId: 'scope-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpReservationListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of reservations for the scope on init', async () => {
    const fixture = await createComponent();

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsGet).toHaveBeenCalledWith({
      scopeId: 'scope-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.reservations().length).toBe(1);
  });

  it('deleteReservation() deletes and reloads the page once confirmed', async () => {
    const fixture = await createComponent();
    reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteReservation(
      fixture.componentInstance.reservations()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdDelete).toHaveBeenCalledWith({
      scopeId: 'scope-1',
      id: 'res-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'conflict' })),
    );

    const deletePromise = fixture.componentInstance.deleteReservation(
      fixture.componentInstance.reservations()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('conflict');
  });

  it('is not a Prefix Delegation scope by default', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.isPrefixDelegationScope()).toBeFalse();
  });

  it('flags itself as a Prefix Delegation scope once the owning scope loads as one', async () => {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesIdGet']);
    scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(
      of({ id: 'scope-1', name: 'prefixes', kind: 'PrefixDelegation' }),
    );
    reservationsApiSpy = jasmine.createSpyObj('DhcpReservationsService', [
      'apiDhcpScopesScopeIdReservationsGet',
      'apiDhcpScopesScopeIdReservationsIdDelete',
    ]);
    reservationsApiSpy.apiDhcpScopesScopeIdReservationsGet.and.returnValue(
      of({ items: [], skip: 0, take: 25, totalCount: 0 }),
    );
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [DhcpReservationListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpReservationsService, useValue: reservationsApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ scopeId: 'scope-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpReservationListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.isPrefixDelegationScope()).toBeTrue();
  });
});
