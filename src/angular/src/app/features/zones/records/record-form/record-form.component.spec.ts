import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DnsResourceRecordTypeEnum, RecordsService, ZonesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { RecordFormComponent } from './record-form.component';

/**
 * This helper waits one macrotask so the component's async loads settle. The app is zoneless, so
 * `whenStable()` does not see them; `dashboard.component.spec.ts` explains this in full.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the record add and edit form: TTL validation, the SOA lock for an empty zone
 * and for an existing SOA, loading a record into the common fields and the type-specific form,
 * and saving. They use TestBed with spy-backed API services and a stubbed ActivatedRoute; the
 * A-record form is mounted for real through NgComponentOutlet.
 */
describe('RecordFormComponent', () => {
  // Hand-shaped spy types; see zone-list.component.spec.ts for why not `jasmine.SpyObj`.
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
   * This helper creates fresh spies with defaults, applies `configureApis` and then creates the
   * component. `ngOnInit` calls the APIs at once, so anything the initial load should see must be
   * set in `configureApis`, not afterwards.
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

  /** This helper returns the address control of the mounted `ARecordFormComponent`, so a test can fill it. */
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

  it('create mode: does not offer SOA among the types when the zone already has records', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' });

    expect(fixture.componentInstance.typeOptions()).not.toContain(DnsResourceRecordTypeEnum.Soa);
    expect(fixture.componentInstance.typeOptions()).toContain(DnsResourceRecordTypeEnum.A);
    const options = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('#record-type option'),
    ).map((option) => option.textContent?.trim());
    expect(options).not.toContain('SOA');
  });

  it('edit mode: does not offer SOA among the types when editing an A record', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1', recordId: 'record-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsIdGet.and.returnValue(
        of({
          id: 'record-1',
          zoneId: 'zone-1',
          name: 'www',
          type: DnsResourceRecordTypeEnum.A,
          timeToLiveSeconds: 300,
          data: { address: '203.0.113.9' },
          isDisabled: false,
          excludeFromSync: false,
        }),
      );
    });

    expect(fixture.componentInstance.typeOptions()).not.toContain(DnsResourceRecordTypeEnum.Soa);
    expect(fixture.componentInstance.typeOptions()).toContain(DnsResourceRecordTypeEnum.A);
  });

  it('create mode: offers only SOA, selected and disabled, for an empty zone', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsGet.and.returnValue(
        of({ items: [], skip: 0, take: 1, totalCount: 0 }),
      );
    });

    expect(fixture.componentInstance.typeOptions()).toEqual([DnsResourceRecordTypeEnum.Soa]);
    expect(fixture.componentInstance.form.controls.type.value).toBe(DnsResourceRecordTypeEnum.Soa);
    expect(fixture.componentInstance.form.controls.type.disabled).toBeTrue();
  });

  it('edit mode: offers only SOA, selected and disabled, when editing the SOA', async () => {
    const fixture = await createComponent({ zoneId: 'zone-1', recordId: 'record-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsIdGet.and.returnValue(
        of({
          id: 'record-1',
          zoneId: 'zone-1',
          name: '@',
          type: DnsResourceRecordTypeEnum.Soa,
          timeToLiveSeconds: 3600,
          data: {},
          isDisabled: false,
          excludeFromSync: false,
        }),
      );
    });

    expect(fixture.componentInstance.typeOptions()).toEqual([DnsResourceRecordTypeEnum.Soa]);
    expect(fixture.componentInstance.form.controls.type.value).toBe(DnsResourceRecordTypeEnum.Soa);
    expect(fixture.componentInstance.form.controls.type.disabled).toBeTrue();
  });

  it("save() in create mode POSTs the common fields plus the mounted type form's own data, then navigates to the record list", async () => {
    const fixture = await createComponent({ zoneId: 'zone-1' }, () => {
      recordsApiSpy.apiZonesZoneIdRecordsPost.and.returnValue(
        of({ id: 'new-record', zoneId: 'zone-1' }),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'www', timeToLiveSeconds: 600 });

    // The default type is A, so NgComponentOutlet mounts ARecordFormComponent. Filling its
    // address field exercises the dynamic mount, not a stand-in.
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
