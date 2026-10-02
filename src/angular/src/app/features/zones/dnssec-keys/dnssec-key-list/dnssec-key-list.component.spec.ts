import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  DnssecAlgorithmEnum,
  DnssecKeyLifecycleStateEnum,
  DnssecKeyRoleEnum,
  DnssecKeysService,
  ZonesService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DnssecKeyListComponent } from './dnssec-key-list.component';
import { AuthService } from '../../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNSSEC key list: loading and paging, retire/reactivate, delete, the
 * rollover and records dialogs, the parent DS confirmation, and when each action is offered. TestBed
 * with spy-backed API services, a stubbed ActivatedRoute and a stubbed `AuthService.hasPermission`.
 */
describe('DnssecKeyListComponent', () => {
  // The permissions the signed-in test user lacks; it holds every other one, so each test sees
  // the page as an administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  let dnssecKeysApiSpy: {
    apiZonesZoneIdDnssecKeysGet: jasmine.Spy;
    apiZonesZoneIdDnssecKeysIdPatch: jasmine.Spy;
    apiZonesZoneIdDnssecKeysIdDelete: jasmine.Spy;
    apiZonesZoneIdDnssecKeysIdRollPost: jasmine.Spy;
    apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost: jasmine.Spy;
  };
  let zonesApiSpy: { apiZonesIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  /** This helper wraps the given keys in a one-page result. */
  function pageOf(items: unknown[]) {
    return { items, skip: 0, take: 25, totalCount: items.length };
  }

  async function createComponent(
    items: unknown[] = [
      {
        id: 'key-1',
        zoneId: 'zone-1',
        role: DnssecKeyRoleEnum.Ksk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keyTag: 12345,
        isRetired: false,
        lifecycleState: DnssecKeyLifecycleStateEnum.Active,
      },
    ],
  ) {
    dnssecKeysApiSpy = jasmine.createSpyObj('DnssecKeysService', [
      'apiZonesZoneIdDnssecKeysGet',
      'apiZonesZoneIdDnssecKeysIdPatch',
      'apiZonesZoneIdDnssecKeysIdDelete',
      'apiZonesZoneIdDnssecKeysIdRollPost',
      'apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost',
    ]);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    zonesApiSpy.apiZonesIdGet.and.returnValue(of({ id: 'zone-1', name: 'example.com' }));
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysGet.and.returnValue(of(pageOf(items)));

    await TestBed.configureTestingModule({
      imports: [DnssecKeyListComponent],
      providers: [
        provideRouter([]),
        { provide: DnssecKeysService, useValue: dnssecKeysApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ zoneId: 'zone-1' }) } },
        },
      ],
    }).compileComponents();

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(DnssecKeyListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the owning zone name and the first page of keys on init', async () => {
    const fixture = await createComponent();

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysGet).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.zoneName()).toBe('example.com');
    expect(fixture.componentInstance.keys().length).toBe(1);
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysGet).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      skip: 10,
      take: 10,
    });
  });

  it('toggleRetired() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleRetired(fixture.componentInstance.keys()[0]);

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdPatch).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'key-1',
      body: { isRetired: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteKey() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdDelete).not.toHaveBeenCalled();
  });

  it('deleteKey() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdDelete).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'key-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteKey(fixture.componentInstance.keys()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it('rollKey() opens the rollover dialog for that key instead of calling the API itself', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.rollKey(fixture.componentInstance.keys()[0]);
    fixture.detectChanges();

    expect(fixture.componentInstance.rolloverDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.rolloverKey()?.id).toBe('key-1');
    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdRollPost).not.toHaveBeenCalled();
  });

  it('onRolled() announces the incoming key and reloads the page', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onRolled({ id: 'key-2', keyTag: 54321 });

    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('54321'));
    // The reload fetches the incoming key's row, so the new Published or DoubleSignature key
    // appears.
    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysGet).toHaveBeenCalledTimes(2);
  });

  it('the records action opens the records dialog for that key', async () => {
    const fixture = await createComponent();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[aria-label="Show DS and DNSKEY records"]',
    );
    button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.recordsDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.recordsKey()?.id).toBe('key-1');
  });

  it('the "start rollover" action only appears for a key that is currently Active', async () => {
    const fixture = await createComponent([
      {
        id: 'key-1',
        zoneId: 'zone-1',
        role: DnssecKeyRoleEnum.Zsk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keyTag: 1,
        isRetired: false,
        lifecycleState: DnssecKeyLifecycleStateEnum.Published,
      },
    ]);

    const rollButton: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      '[aria-label="Start a rollover"]',
    );
    expect(rollButton).toBeNull();
  });

  it('the "start rollover" action appears for a key that is currently Active', async () => {
    const fixture = await createComponent();

    const rollButton: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      '[aria-label="Start a rollover"]',
    );
    expect(rollButton).not.toBeNull();
  });

  it('confirmParentDsUpdated() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const confirmPromise = fixture.componentInstance.confirmParentDsUpdated(
      fixture.componentInstance.keys()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await confirmPromise;

    expect(
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost,
    ).not.toHaveBeenCalled();
  });

  it('confirmParentDsUpdated() confirms and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost.and.returnValue(
      of(undefined),
    );

    const confirmPromise = fixture.componentInstance.confirmParentDsUpdated(
      fixture.componentInstance.keys()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await confirmPromise;

    expect(
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost,
    ).toHaveBeenCalledWith({ zoneId: 'zone-1', id: 'key-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when confirming the parent DS update fails', async () => {
    const fixture = await createComponent();
    dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: 'not mid-rollover' })),
    );

    const confirmPromise = fixture.componentInstance.confirmParentDsUpdated(
      fixture.componentInstance.keys()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await confirmPromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('not mid-rollover');
  });

  it('the "confirm parent DS updated" action only appears for a KSK mid-rollover, still unconfirmed', async () => {
    const fixture = await createComponent([
      {
        id: 'key-1',
        zoneId: 'zone-1',
        role: DnssecKeyRoleEnum.Ksk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keyTag: 1,
        isRetired: false,
        lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
        parentDsConfirmedAtUtc: null,
      },
    ]);

    const confirmButton: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      '[aria-label="Confirm parent DS updated"]',
    );
    expect(confirmButton).not.toBeNull();
  });

  it('the "confirm parent DS updated" action does not appear once already confirmed', async () => {
    const fixture = await createComponent([
      {
        id: 'key-1',
        zoneId: 'zone-1',
        role: DnssecKeyRoleEnum.Ksk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keyTag: 1,
        isRetired: false,
        lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
        parentDsConfirmedAtUtc: '2026-09-17T00:00:00Z',
      },
    ]);

    const confirmButton: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      '[aria-label="Confirm parent DS updated"]',
    );
    expect(confirmButton).toBeNull();
  });

  it('the "confirm parent DS updated" action does not appear for a ZSK, even mid-rollover', async () => {
    const fixture = await createComponent([
      {
        id: 'key-1',
        zoneId: 'zone-1',
        role: DnssecKeyRoleEnum.Zsk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keyTag: 1,
        isRetired: false,
        lifecycleState: DnssecKeyLifecycleStateEnum.DoubleSignature,
        parentDsConfirmedAtUtc: null,
      },
    ]);

    const confirmButton: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      '[aria-label="Confirm parent DS updated"]',
    );
    expect(confirmButton).toBeNull();
  });
});
