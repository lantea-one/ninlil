import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DnsResourceRecordTypeEnum, RecordsService, ZonesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { RecordFormComponent } from './record-form.component';

/**
 * See `dashboard.component.spec.ts`'s own identical helper for why a zoneless app's tests need
 * this rather than `ComponentFixture.whenStable()`.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('RecordFormComponent', () => {
  // Loosely-typed spies, not `jasmine.SpyObj<RecordsService>`/`<ZonesService>` -- see
  // `zone-list.component.spec.ts`'s own identical note for why (the generated services' own
  // multi-overload methods otherwise infer the wrong, `HttpEvent`-wrapped return type).
  let recordsApiSpy: {
    apiZonesZoneIdRecordsGet: jasmine.Spy;
    apiZonesZoneIdRecordsIdGet: jasmine.Spy;
    apiZonesZoneIdRecordsPost: jasmine.Spy;
    apiZonesZoneIdRecordsIdPut: jasmine.Spy;
  };
  let zonesApiSpy: { apiZonesIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  /**
   * This helper creates fresh spies with sensible defaults, then applies `configureApis` (if any)
   * *before* the component is ever constructed -- necessary because `ngOnInit` reads these APIs
   * immediately, so a test overriding a return value only for the *initial* load (as opposed to a
   * later, explicit method call such as `save()`) must configure it before `TestBed.createComponent`
   * runs, not after. Jasmine's own default random test order means a `let` reused across `it`
   * blocks cannot safely be pre-configured by a caller before this function itself creates it.
   */
  async function createComponent(
    params: { zoneId: string; recordId?: string },
    configureApis?: () => void,
  ) {
    recordsApiSpy = jasmine.createSpyObj('RecordsService', [
      'apiZonesZoneIdRecordsGet',
      'apiZonesZoneIdRecordsIdGet',
      'apiZonesZoneIdRecordsPost',
      'apiZonesZoneIdRecordsIdPut',
    ]);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    zonesApiSpy.apiZonesIdGet.and.returnValue(of({ id: params.zoneId, name: 'example.com' }));
    recordsApiSpy.apiZonesZoneIdRecordsGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 3 }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [RecordFormComponent],
      providers: [
        provideRouter([]),
        { provide: RecordsService, useValue: recordsApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(params) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(RecordFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  /** This helper reaches into the dynamically-mounted `ARecordFormComponent` -- see its own call sites for why. */
  function addressControlOf(fixture: { componentInstance: unknown }) {
    return (
      fixture.componentInstance as unknown as {
        dataFormOutlet: () =>
          | {
              componentInstance: {
                form: { controls: { address: { setValue: (v: string) => void } } };
              };
            }
          | undefined;
      }
    ).dataFormOutlet()?.componentInstance.form.controls.address;
  }

  it('rejects a timeToLiveSeconds value above 4294967295, the real 32-bit unsigned backend bound', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' });

    fixture.componentInstance.form.controls.timeToLiveSeconds.setValue(4294967296);
    expect(fixture.componentInstance.form.controls.timeToLiveSeconds.hasError('max')).toBeTrue();

    fixture.componentInstance.form.controls.timeToLiveSeconds.setValue(4294967295);
    expect(fixture.componentInstance.form.controls.timeToLiveSeconds.hasError('max')).toBeFalse();
  });

  it('create mode: leaves the type selector open when the zone already has records', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' });

    expect(fixture.componentInstance.isFirstRecordInZone()).toBeFalse();
    expect(fixture.componentInstance.form.controls.type.disabled).toBeFalse();
    expect(fixture.componentInstance.zoneName()).toBe('example.com');
  });

  it("create mode: forces and locks the type to SOA when the zone has zero records (RecordsController's own bootstrap rule)", async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsGet.and.returnValue(
        of({ items: [], skip: 0, take: 1, totalCount: 0 }),
      );
    });

    expect(fixture.componentInstance.isFirstRecordInZone()).toBeTrue();
    expect(fixture.componentInstance.form.controls.type.value).toBe(DnsResourceRecordTypeEnum.Soa);
    expect(fixture.componentInstance.form.controls.type.disabled).toBeTrue();
  });

  it('edit mode: populates every common field, the selected type, and the type-specific initial data', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1', recordId: 'record-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsIdGet.and.returnValue(
        of({
          id: 'record-1',
          zoneId: 'zone-1',
          name: 'www',
          type: DnsResourceRecordTypeEnum.A,
          timeToLiveSeconds: 300,
          data: { address: '203.0.113.9' },
          isDisabled: true,
          comments: 'existing note',
          excludeFromSync: true,
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.form.getRawValue()).toEqual(
      jasmine.objectContaining({
        name: 'www',
        type: DnsResourceRecordTypeEnum.A,
        timeToLiveSeconds: 300,
        isDisabled: true,
        excludeFromSync: true,
        comments: 'existing note',
      }),
    );
    expect(fixture.componentInstance.initialRecordData()).toEqual({ address: '203.0.113.9' });
  });

  it("edit mode: locks the type selector for the zone's own SOA record", async () => {
    const fixture = await createComponent({ zoneId: 'zone-1', recordId: 'record-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsIdGet.and.returnValue(
        of({
          id: 'record-1',
          zoneId: 'zone-1',
          name: '@',
          type: DnsResourceRecordTypeEnum.Soa,
          timeToLiveSeconds: 3600,
          data: {
            primaryNameServer: 'ns1.example.com.',
            responsiblePersonMailbox: 'admin.example.com.',
            serial: 1,
            refreshSeconds: 3600,
            retrySeconds: 900,
            expireSeconds: 1209600,
            minimumTtlSeconds: 3600,
          },
          isDisabled: false,
          excludeFromSync: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.type.disabled).toBeTrue();
  });

  it("save() in create mode POSTs the common fields plus the mounted type form's own data, then navigates to the record list", async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsPost.and.returnValue(
        of({ id: 'new-record', zoneId: 'zone-1' }),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'www', timeToLiveSeconds: 600 });

    // The default selected type is A, so ARecordFormComponent is the real component mounted by
    // NgComponentOutlet -- filling its own address field exercises the real dynamic-mount wiring,
    // not a stand-in.
    addressControlOf(fixture)?.setValue('203.0.113.5');
    fixture.detectChanges();

    await fixture.componentInstance.save();

    expect(recordsApiSpy.apiZonesZoneIdRecordsPost).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      recordWriteRequest: jasmine.objectContaining({
        name: 'www',
        type: DnsResourceRecordTypeEnum.A,
        timeToLiveSeconds: 600,
        data: { address: '203.0.113.5' },
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/zones', 'zone-1', 'records']);
  });

  it('save() shows an error message and does not navigate when the backend rejects the request', async () => {
    const { HttpErrorResponse } = await import('@angular/common/http');
    const fixture = await createComponent({ zoneId: 'zone-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ error: 'A conflicting record already exists.', status: 409 }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'www' });

    addressControlOf(fixture)?.setValue('203.0.113.5');
    fixture.detectChanges();

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('A conflicting record already exists.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' });
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(recordsApiSpy.apiZonesZoneIdRecordsPost).not.toHaveBeenCalled();
  });
});
