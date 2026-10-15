import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DhcpReservationsService, DhcpScopesService } from '../../../../../api-client';
import { NotificationService } from '../../../../../shared/notification/notification.service';
import { DhcpReservationFormComponent } from './dhcp-reservation-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the reservation form: add and edit modes, validation, and refusing to save
 * for a Prefix Delegation scope. TestBed with spy-backed reservation and scope APIs and a stubbed route.
 */
describe('DhcpReservationFormComponent', () => {
  let reservationsApiSpy: {
    apiDhcpScopesScopeIdReservationsPost: jasmine.Spy;
    apiDhcpScopesScopeIdReservationsIdPut: jasmine.Spy;
    apiDhcpScopesScopeIdReservationsIdGet: jasmine.Spy;
  };
  let scopesApiSpy: { apiDhcpScopesIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(
    reservationId: string | null,
    configureApis?: () => void,
    scopeKind: string = 'AddressAssignment',
  ) {
    reservationsApiSpy = jasmine.createSpyObj('DhcpReservationsService', [
      'apiDhcpScopesScopeIdReservationsPost',
      'apiDhcpScopesScopeIdReservationsIdPut',
      'apiDhcpScopesScopeIdReservationsIdGet',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesIdGet']);
    scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(
      of({ id: 'scope-1', name: 'lan', kind: scopeKind }),
    );
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [DhcpReservationFormComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpReservationsService, useValue: reservationsApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                scopeId: 'scope-1',
                ...(reservationId ? { reservationId } : {}),
              }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(DhcpReservationFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is in create mode with no reservationId route param', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isEditMode).toBeFalse();
  });

  it('posts a new reservation and navigates back to the reservation list', async () => {
    const fixture = await createComponent(null, () => {
      reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost.and.returnValue(of({ id: 'res-1' }));
    });

    fixture.componentInstance.form.patchValue({
      clientIdentifier: 'aa:bb:cc:dd:ee:ff',
      ipAddress: '192.168.1.50',
    });

    await fixture.componentInstance.save();

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost).toHaveBeenCalledWith(
      jasmine.objectContaining({ scopeId: 'scope-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/dhcp-scopes', 'scope-1', 'reservations']);
  });

  it('loads an existing reservation in edit mode and PUTs on save', async () => {
    const fixture = await createComponent('res-1', () => {
      reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdGet.and.returnValue(
        of({
          id: 'res-1',
          scopeId: 'scope-1',
          clientIdentifier: 'aa:bb:cc:dd:ee:ff',
          ipAddress: '192.168.1.50',
          hostName: 'printer',
          isDisabled: false,
          comments: null,
        }),
      );
      reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdPut.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.form.controls.clientIdentifier.value).toBe(
      'aa:bb:cc:dd:ee:ff',
    );

    await fixture.componentInstance.save();

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({ scopeId: 'scope-1', id: 'res-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ clientIdentifier: '', ipAddress: '' });

    await fixture.componentInstance.save();

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost).not.toHaveBeenCalled();
  });

  it('flags itself as a Prefix Delegation scope once the owning scope loads as one', async () => {
    const fixture = await createComponent(null, undefined, 'PrefixDelegation');

    expect(fixture.componentInstance.isPrefixDelegationScope()).toBeTrue();
  });

  it('save() does nothing against a Prefix Delegation scope, even with an otherwise-valid form', async () => {
    const fixture = await createComponent(
      null,
      () => {
        reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost.and.returnValue(
          of({ id: 'res-1' }),
        );
      },
      'PrefixDelegation',
    );

    fixture.componentInstance.form.patchValue({
      clientIdentifier: 'aa:bb:cc:dd:ee:ff',
      ipAddress: '192.168.1.50',
    });

    await fixture.componentInstance.save();

    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost).not.toHaveBeenCalled();
  });

  it('refuses an identifier in no accepted form, and accepts every accepted spelling', async () => {
    const fixture = await createComponent(null);
    const form = fixture.componentInstance.form;
    form.patchValue({ ipAddress: '192.168.1.50', clientIdentifier: 'aa:bb-cc' });

    expect(form.controls.clientIdentifier.hasError('clientIdentifier')).toBeTrue();
    expect(form.invalid).toBeTrue();
    await fixture.componentInstance.save();
    expect(reservationsApiSpy.apiDhcpScopesScopeIdReservationsPost).not.toHaveBeenCalled();

    for (const value of [
      'AA:BB:CC:DD:EE:FF',
      'aa-bb-cc-dd-ee-ff',
      'aabb.ccdd.eeff',
      'aabbccddeeff',
    ]) {
      form.patchValue({ clientIdentifier: value });
      expect(form.controls.clientIdentifier.valid).withContext(value).toBeTrue();
    }
  });
});
